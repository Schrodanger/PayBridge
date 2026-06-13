using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PayBridge.PaymentApi.Contracts;
using PayBridge.PaymentApi.Observability;
using PayBridge.PaymentApi.Persistence;
using PayBridge.PaymentApi.Services;
using PayBridge.Shared.Domain;
using PayBridge.Shared.Messaging;

namespace PayBridge.PaymentApi.Tests.Unit;

public class PaymentServiceTests
{
    private static (PaymentService svc, PaymentDbContext db, IFraudClient fraud, IProviderClient provider, IPaymentEventPublisher publisher, IIdempotencyStore idem, IKillSwitch kill)
        Build(bool killActive = false)
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase($"payments-{Guid.NewGuid()}")
            .Options;
        var db = new PaymentDbContext(options);

        var fraud = Substitute.For<IFraudClient>();
        var provider = Substitute.For<IProviderClient>();
        var publisher = Substitute.For<IPaymentEventPublisher>();
        var idem = Substitute.For<IIdempotencyStore>();
        var kill = Substitute.For<IKillSwitch>();
        kill.IsPaymentsDisabledAsync(Arg.Any<CancellationToken>()).Returns(killActive);

        var metrics = new PaymentMetrics(new TestMeterFactory());
        var activity = new ActivitySource("PayBridge.Tests");

        var svc = new PaymentService(db, fraud, provider, publisher, idem, kill, metrics, activity, NullLogger<PaymentService>.Instance);
        return (svc, db, fraud, provider, publisher, idem, kill);
    }

    private static CreatePaymentRequest SampleRequest(string idemKey = "key-1") => new(
        MerchantId: "merch-1",
        IdempotencyKey: idemKey,
        Amount: 99.95m,
        Currency: "USD",
        CustomerEmail: "buyer@example.com",
        Method: PaymentMethod.CreditCard,
        Metadata: null);

    [Fact]
    public async Task Creates_payment_and_submits_when_fraud_approves_and_provider_accepts()
    {
        var (svc, db, fraud, provider, publisher, _, _) = Build();
        fraud.CheckAsync(Arg.Any<Payment>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new FraudResult(true, 0.1, "ok"));
        provider.SubmitAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>())
                .Returns(new ProviderSubmissionResult(true, "prov_123", null));

        var (resp, created) = await svc.CreateAsync(SampleRequest(), "tenant-a", default);

        created.Should().BeTrue();
        resp.Status.Should().Be(PaymentStatus.Submitted);
        resp.ProviderTransactionId.Should().Be("prov_123");
        (await db.Payments.SingleAsync()).Status.Should().Be(PaymentStatus.Submitted);
        await publisher.Received(1).PublishAsync(
            Arg.Is<PaymentEvent>(e => e.EventType == PaymentEventTypes.Initiated),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Marks_failed_when_fraud_rejects_and_does_not_call_provider()
    {
        var (svc, db, fraud, provider, publisher, _, _) = Build();
        fraud.CheckAsync(Arg.Any<Payment>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new FraudResult(false, 0.95, "high_risk_pattern"));

        var (resp, _) = await svc.CreateAsync(SampleRequest(), "tenant-a", default);

        resp.Status.Should().Be(PaymentStatus.Failed);
        await provider.DidNotReceive().SubmitAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
        await publisher.Received(1).PublishAsync(
            Arg.Is<PaymentEvent>(e => e.EventType == PaymentEventTypes.Failed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_cached_response_on_idempotency_hit()
    {
        var (svc, db, fraud, provider, _, idem, _) = Build();
        var cached = new PaymentResponse(Guid.NewGuid(), "merch-1", 99.95m, "USD",
            PaymentStatus.Completed, "prov_abc", null, DateTime.UtcNow, DateTime.UtcNow);
        idem.TryGetAsync("merch-1", "key-1", Arg.Any<CancellationToken>()).Returns(cached);

        var (resp, created) = await svc.CreateAsync(SampleRequest(), "tenant-a", default);

        created.Should().BeFalse();
        resp.Should().BeEquivalentTo(cached);
        await fraud.DidNotReceiveWithAnyArgs().CheckAsync(default!, default!, default);
        await provider.DidNotReceiveWithAnyArgs().SubmitAsync(default!, default);
        db.Payments.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_with_PaymentRejectedException_when_kill_switch_active()
    {
        var (svc, _, _, _, _, _, _) = Build(killActive: true);

        Func<Task> act = () => svc.CreateAsync(SampleRequest(), "tenant-a", default);

        await act.Should().ThrowAsync<PaymentRejectedException>();
    }

    [Fact]
    public async Task ApplyWebhook_marks_completed_and_publishes_event()
    {
        var (svc, db, fraud, provider, publisher, _, _) = Build();
        fraud.CheckAsync(Arg.Any<Payment>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new FraudResult(true, 0.1, "ok"));
        provider.SubmitAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>())
                .Returns(new ProviderSubmissionResult(true, "prov_xyz", null));
        var (created, _) = await svc.CreateAsync(SampleRequest(), "tenant-a", default);

        publisher.ClearReceivedCalls();
        var callback = new ProviderWebhookCallback("prov_xyz", "SUCCESS", DateTime.UtcNow,
            new Dictionary<string, string> { ["paybridge_payment_id"] = created.Id.ToString() });

        var payment = await svc.ApplyWebhookAsync(callback, default);

        payment!.Status.Should().Be(PaymentStatus.Completed);
        payment.CompletedAt.Should().NotBeNull();
        await publisher.Received(1).PublishAsync(
            Arg.Is<PaymentEvent>(e => e.EventType == PaymentEventTypes.Completed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyWebhook_treats_duplicate_callback_as_noop()
    {
        var (svc, _, fraud, provider, publisher, _, _) = Build();
        fraud.CheckAsync(Arg.Any<Payment>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new FraudResult(true, 0.1, "ok"));
        provider.SubmitAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>())
                .Returns(new ProviderSubmissionResult(true, "prov_xyz", null));
        var (created, _) = await svc.CreateAsync(SampleRequest(), "tenant-a", default);
        var callback = new ProviderWebhookCallback("prov_xyz", "SUCCESS", DateTime.UtcNow,
            new Dictionary<string, string> { ["paybridge_payment_id"] = created.Id.ToString() });
        await svc.ApplyWebhookAsync(callback, default);
        publisher.ClearReceivedCalls();

        // Same callback delivered again
        var second = await svc.ApplyWebhookAsync(callback, default);

        second!.Status.Should().Be(PaymentStatus.Completed);
        await publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }
}

internal sealed class TestMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options) => new(options);
    public void Dispose() { }
}
