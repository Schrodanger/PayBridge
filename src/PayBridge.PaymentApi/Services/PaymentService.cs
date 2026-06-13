using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PayBridge.PaymentApi.Contracts;
using PayBridge.PaymentApi.Observability;
using PayBridge.PaymentApi.Persistence;
using PayBridge.Shared.Domain;
using PayBridge.Shared.Messaging;
using PayBridge.Shared.Observability;
using PayBridge.Shared.Security;

namespace PayBridge.PaymentApi.Services;

public sealed class PaymentService
{
    private readonly PaymentDbContext _db;
    private readonly IFraudClient _fraud;
    private readonly IProviderClient _provider;
    private readonly IPaymentEventPublisher _events;
    private readonly IIdempotencyStore _idempotency;
    private readonly IKillSwitch _killSwitch;
    private readonly PaymentMetrics _metrics;
    private readonly ActivitySource _activity;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        PaymentDbContext db,
        IFraudClient fraud,
        IProviderClient provider,
        IPaymentEventPublisher events,
        IIdempotencyStore idempotency,
        IKillSwitch killSwitch,
        PaymentMetrics metrics,
        ActivitySource activity,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _fraud = fraud;
        _provider = provider;
        _events = events;
        _idempotency = idempotency;
        _killSwitch = killSwitch;
        _metrics = metrics;
        _activity = activity;
        _logger = logger;
    }

    public async Task<(PaymentResponse Response, bool Created)> CreateAsync(
        CreatePaymentRequest request,
        string tenantId,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var rootActivity = _activity.StartActivity("payment.create");
        rootActivity?.SetTag(Telemetry.TraceTags.MerchantId, request.MerchantId);
        rootActivity?.SetTag(Telemetry.TraceTags.TenantId, tenantId);
        rootActivity?.SetTag(Telemetry.TraceTags.IdempotencyKey, request.IdempotencyKey);

        // 1. Idempotency cache hit?
        var cached = await _idempotency.TryGetAsync(request.MerchantId, request.IdempotencyKey, ct);
        if (cached is not null)
        {
            _metrics.IdempotencyHit();
            _logger.LogInformation(
                "Idempotency cache hit for merchant {MerchantId} key {IdempotencyKey} -> payment {PaymentId}",
                request.MerchantId, request.IdempotencyKey, cached.Id);
            _metrics.PaymentLatency(sw.Elapsed.TotalMilliseconds, "idempotent");
            return (cached, false);
        }

        // 2. Kill switch?
        if (await _killSwitch.IsPaymentsDisabledAsync(ct))
        {
            _metrics.KillSwitchRejected();
            _logger.LogWarning("Kill switch active — rejecting payment from merchant {MerchantId}", request.MerchantId);
            throw new PaymentRejectedException("payment processing temporarily disabled");
        }

        // 3. Create the payment row in Created state.
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            MerchantId = request.MerchantId,
            TenantId = tenantId,
            IdempotencyKey = request.IdempotencyKey,
            Amount = request.Amount,
            Currency = request.Currency,
            Method = request.Method,
            Status = PaymentStatus.Created,
            CreatedAt = DateTime.UtcNow,
            OriginatingTraceId = Activity.Current?.TraceId.ToString()
        };

        _db.Payments.Add(payment);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost race against another in-flight request with the same idempotency key — return the winner.
            var existing = await GetExistingAsync(request, ct);
            if (existing is null)
            {
                throw;
            }
            _metrics.IdempotencyHit();
            var existingResponse = PaymentResponse.FromEntity(existing);
            await _idempotency.SaveAsync(request.MerchantId, request.IdempotencyKey, existingResponse, ct);
            return (existingResponse, false);
        }

        rootActivity?.SetTag(Telemetry.TraceTags.PaymentId, payment.Id);
        _metrics.PaymentCreated(request.MerchantId, request.Currency, request.Method.ToString());

        _logger.LogInformation(
            "Payment {PaymentId} created for merchant {MerchantId}, amount={Amount} {Currency}, customer={CustomerEmail}",
            payment.Id, request.MerchantId, request.Amount, request.Currency, PiiMasking.MaskEmail(request.CustomerEmail));

        // 4. Fraud check.
        payment.Status = PaymentStatus.FraudChecking;
        await _db.SaveChangesAsync(ct);

        FraudResult fraud;
        try
        {
            fraud = await _fraud.CheckAsync(payment, request.CustomerEmail, ct);
        }
        catch (Exception)
        {
            // Fraud check is critical — without it we cannot decide. Mark failed and short-circuit.
            await MarkFailedAsync(payment, "fraud_check_unavailable", ct);
            var failed = PaymentResponse.FromEntity(payment);
            await _idempotency.SaveAsync(request.MerchantId, request.IdempotencyKey, failed, ct);
            _metrics.PaymentLatency(sw.Elapsed.TotalMilliseconds, "failed");
            return (failed, true);
        }

        if (!fraud.Approved)
        {
            await MarkFailedAsync(payment, $"fraud_rejected:{fraud.Reason}", ct);
            await _events.PublishAsync(BuildEvent(payment, PaymentEventTypes.Failed), ct);
            var failed = PaymentResponse.FromEntity(payment);
            await _idempotency.SaveAsync(request.MerchantId, request.IdempotencyKey, failed, ct);
            _metrics.PaymentLatency(sw.Elapsed.TotalMilliseconds, "failed");
            return (failed, true);
        }

        // 5. Submit to provider.
        ProviderSubmissionResult submission;
        try
        {
            submission = await _provider.SubmitAsync(payment, ct);
        }
        catch (Exception)
        {
            await MarkFailedAsync(payment, "provider_unavailable", ct);
            await _events.PublishAsync(BuildEvent(payment, PaymentEventTypes.Failed), ct);
            var failed = PaymentResponse.FromEntity(payment);
            await _idempotency.SaveAsync(request.MerchantId, request.IdempotencyKey, failed, ct);
            _metrics.PaymentLatency(sw.Elapsed.TotalMilliseconds, "failed");
            return (failed, true);
        }

        payment.Status = submission.Accepted ? PaymentStatus.Submitted : PaymentStatus.Failed;
        payment.ProviderTransactionId = submission.ProviderTransactionId;
        if (!submission.Accepted)
        {
            payment.FailureReason = $"provider_rejected:{submission.Reason}";
            payment.CompletedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        // 6. Publish the initiated event (the webhook handler will publish completed / failed later).
        var initEventType = submission.Accepted ? PaymentEventTypes.Initiated : PaymentEventTypes.Failed;
        await _events.PublishAsync(BuildEvent(payment, initEventType), ct);

        var response = PaymentResponse.FromEntity(payment);
        await _idempotency.SaveAsync(request.MerchantId, request.IdempotencyKey, response, ct);

        _metrics.PaymentLatency(
            sw.Elapsed.TotalMilliseconds,
            submission.Accepted ? "submitted" : "failed");

        return (response, true);
    }

    public async Task<Payment?> ApplyWebhookAsync(ProviderWebhookCallback callback, CancellationToken ct)
    {
        if (callback.Metadata is null
            || !callback.Metadata.TryGetValue("paybridge_payment_id", out var idStr)
            || !Guid.TryParse(idStr, out var paymentId))
        {
            _metrics.WebhookReceived("malformed");
            return null;
        }

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null)
        {
            _metrics.WebhookReceived("unknown_payment");
            return null;
        }

        // Duplicate webhook: if we've already settled, treat as success-no-op (HTTP 200) so the
        // provider stops retrying. Most providers will spam callbacks until they get a 2xx.
        if (payment.Status is PaymentStatus.Completed or PaymentStatus.Failed)
        {
            _metrics.WebhookReceived("duplicate");
            return payment;
        }

        var nowUtc = DateTime.UtcNow;
        var success = string.Equals(callback.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase);

        payment.Status = success ? PaymentStatus.Completed : PaymentStatus.Failed;
        payment.ProviderTransactionId ??= callback.ProviderTransactionId;
        payment.CompletedAt = nowUtc;
        if (!success)
        {
            payment.FailureReason = $"provider_callback:{callback.Status}";
        }
        await _db.SaveChangesAsync(ct);

        _metrics.WebhookReceived(success ? "completed" : "failed");
        if (success)
        {
            _metrics.PaymentCompleted(payment.MerchantId, payment.Currency);
        }
        else
        {
            _metrics.PaymentFailed(payment.MerchantId, payment.Currency, "provider_callback");
        }

        await _events.PublishAsync(
            BuildEvent(payment, success ? PaymentEventTypes.Completed : PaymentEventTypes.Failed),
            ct);

        return payment;
    }

    private static PaymentEvent BuildEvent(Payment p, string eventType) => new(
        p.Id,
        p.MerchantId,
        p.TenantId,
        eventType,
        p.Amount,
        p.Currency,
        p.ProviderTransactionId,
        p.FailureReason,
        DateTime.UtcNow);

    private async Task<Payment?> GetExistingAsync(CreatePaymentRequest request, CancellationToken ct) =>
        await _db.Payments.AsNoTracking().FirstOrDefaultAsync(
            p => p.MerchantId == request.MerchantId && p.IdempotencyKey == request.IdempotencyKey,
            ct);

    private async Task MarkFailedAsync(Payment p, string reason, CancellationToken ct)
    {
        p.Status = PaymentStatus.Failed;
        p.FailureReason = reason;
        p.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _metrics.PaymentFailed(p.MerchantId, p.Currency, BucketReason(reason));
    }

    private static string BucketReason(string reason) => reason switch
    {
        var r when r.StartsWith("fraud", StringComparison.OrdinalIgnoreCase) => "fraud",
        var r when r.StartsWith("provider", StringComparison.OrdinalIgnoreCase) => "provider",
        _ => "other"
    };
}

public class PaymentRejectedException(string reason) : Exception(reason);
