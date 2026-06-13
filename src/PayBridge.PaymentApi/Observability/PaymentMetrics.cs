using System.Diagnostics.Metrics;
using PayBridge.Shared.Observability;

namespace PayBridge.PaymentApi.Observability;

/// <summary>
/// All custom metrics for the Payment API.
/// Label sets are intentionally narrow (merchant_id, currency, method, status_bucket) to
/// keep cardinality under control at production traffic.
/// </summary>
public sealed class PaymentMetrics
{
    private readonly Counter<long> _paymentsCreated;
    private readonly Counter<long> _paymentsCompleted;
    private readonly Counter<long> _paymentsFailed;
    private readonly Histogram<double> _paymentLatencyMs;

    private readonly Counter<long> _fraudChecks;
    private readonly Counter<long> _providerCalls;
    private readonly Histogram<double> _providerCallMs;
    private readonly Counter<long> _webhooksReceived;
    private readonly Counter<long> _eventsPublished;
    private readonly Counter<long> _idempotencyHits;
    private readonly Counter<long> _killSwitchRejections;

    public PaymentMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Telemetry.MeterName);

        _paymentsCreated = meter.CreateCounter<long>(Telemetry.Metrics.PaymentsCreated);
        _paymentsCompleted = meter.CreateCounter<long>(Telemetry.Metrics.PaymentsCompleted);
        _paymentsFailed = meter.CreateCounter<long>(Telemetry.Metrics.PaymentsFailed);
        _paymentLatencyMs = meter.CreateHistogram<double>(
            Telemetry.Metrics.PaymentLatency, unit: "ms", description: "End-to-end latency of POST /api/payments");

        _fraudChecks = meter.CreateCounter<long>(Telemetry.Metrics.FraudChecks);
        _providerCalls = meter.CreateCounter<long>(Telemetry.Metrics.ProviderCalls);
        _providerCallMs = meter.CreateHistogram<double>(
            Telemetry.Metrics.ProviderCallDuration, unit: "ms", description: "Outbound provider HTTP call duration");

        _webhooksReceived = meter.CreateCounter<long>(Telemetry.Metrics.WebhooksReceived);
        _eventsPublished = meter.CreateCounter<long>(Telemetry.Metrics.EventsPublished);
        _idempotencyHits = meter.CreateCounter<long>(Telemetry.Metrics.IdempotencyHits);
        _killSwitchRejections = meter.CreateCounter<long>(Telemetry.Metrics.KillSwitchRejections);
    }

    public void PaymentCreated(string merchantId, string currency, string method) =>
        _paymentsCreated.Add(1, Tag("merchant", merchantId), Tag("currency", currency), Tag("method", method));

    public void PaymentCompleted(string merchantId, string currency) =>
        _paymentsCompleted.Add(1, Tag("merchant", merchantId), Tag("currency", currency));

    public void PaymentFailed(string merchantId, string currency, string reasonBucket) =>
        _paymentsFailed.Add(1, Tag("merchant", merchantId), Tag("currency", currency), Tag("reason", reasonBucket));

    public void PaymentLatency(double ms, string outcome) =>
        _paymentLatencyMs.Record(ms, Tag("outcome", outcome));

    public void FraudCheck(string outcome) =>
        _fraudChecks.Add(1, Tag("outcome", outcome));

    public void ProviderCall(string provider, string outcome) =>
        _providerCalls.Add(1, Tag("provider", provider), Tag("outcome", outcome));

    public void ProviderCallDuration(string provider, double ms) =>
        _providerCallMs.Record(ms, Tag("provider", provider));

    public void WebhookReceived(string outcome) =>
        _webhooksReceived.Add(1, Tag("outcome", outcome));

    public void EventPublished(string eventType) =>
        _eventsPublished.Add(1, Tag("event_type", eventType));

    public void IdempotencyHit() => _idempotencyHits.Add(1);
    public void KillSwitchRejected() => _killSwitchRejections.Add(1);

    private static KeyValuePair<string, object?> Tag(string k, object? v) => new(k, v);
}
