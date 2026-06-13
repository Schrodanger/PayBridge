using System.Diagnostics;
using System.Diagnostics.Metrics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using PayBridge.SettlementConsumer.Persistence;
using PayBridge.Shared.Domain;
using PayBridge.Shared.Messaging;
using PayBridge.Shared.Observability;

namespace PayBridge.SettlementConsumer.Consumers;

public sealed class PaymentEventConsumer : IConsumer<PaymentEvent>
{
    private readonly SettlementDbContext _db;
    private readonly ILogger<PaymentEventConsumer> _logger;
    private readonly Counter<long> _consumed;
    private readonly Counter<long> _persisted;
    private readonly Counter<long> _duplicates;
    private readonly ActivitySource _activity;

    public PaymentEventConsumer(
        SettlementDbContext db,
        ILogger<PaymentEventConsumer> logger,
        IMeterFactory meterFactory,
        ActivitySource activity)
    {
        _db = db;
        _logger = logger;
        _activity = activity;

        var meter = meterFactory.Create(Telemetry.MeterName);
        _consumed = meter.CreateCounter<long>(Telemetry.Metrics.EventsConsumed);
        _persisted = meter.CreateCounter<long>("paybridge.settlements.persisted");
        _duplicates = meter.CreateCounter<long>("paybridge.settlements.duplicates");
    }

    public async Task Consume(ConsumeContext<PaymentEvent> ctx)
    {
        var evt = ctx.Message;
        using var span = _activity.StartActivity("settlement.persist");
        span?.SetTag(Telemetry.TraceTags.PaymentId, evt.PaymentId);
        span?.SetTag(Telemetry.TraceTags.EventType, evt.EventType);

        _consumed.Add(1, new KeyValuePair<string, object?>("event_type", evt.EventType));

        // We only settle terminal events. Initiated is informational.
        if (evt.EventType is not (PaymentEventTypes.Completed or PaymentEventTypes.Failed))
        {
            return;
        }

        var status = evt.EventType == PaymentEventTypes.Completed
            ? PaymentStatus.Completed
            : PaymentStatus.Failed;

        // Idempotent insert: rely on unique index to swallow duplicates from MQ redelivery.
        var record = new SettlementRecord
        {
            PaymentId = evt.PaymentId,
            MerchantId = evt.MerchantId,
            TenantId = evt.TenantId,
            Amount = evt.Amount,
            Currency = evt.Currency,
            FinalStatus = status,
            ProviderTransactionId = evt.ProviderTransactionId,
            EventTimestamp = evt.Timestamp,
            PersistedAt = DateTime.UtcNow
        };

        _db.Settlements.Add(record);
        try
        {
            await _db.SaveChangesAsync(ctx.CancellationToken);
            _persisted.Add(1, new KeyValuePair<string, object?>("status", status.ToString()));
            _logger.LogInformation(
                "Persisted settlement for payment {PaymentId} status={Status}",
                evt.PaymentId, status);
        }
        catch (DbUpdateException)
        {
            if (!await ExistsAsync(evt.PaymentId, status, ctx.CancellationToken))
            {
                throw;
            }
            _duplicates.Add(1);
            _logger.LogInformation(
                "Duplicate settlement skipped for payment {PaymentId} status={Status}",
                evt.PaymentId, status);
        }
    }

    private Task<bool> ExistsAsync(Guid paymentId, PaymentStatus status, CancellationToken ct) =>
        _db.Settlements.AnyAsync(s => s.PaymentId == paymentId && s.FinalStatus == status, ct);
}
