namespace PayBridge.Shared.Observability;

/// <summary>
/// Single place where activity-source and meter names live so every service
/// publishes traces / metrics under predictable names.
/// </summary>
public static class Telemetry
{
    public const string ActivitySourceName = "PayBridge";
    public const string MeterName = "PayBridge";

    public static class TraceTags
    {
        public const string PaymentId = "paybridge.payment.id";
        public const string MerchantId = "paybridge.merchant.id";
        public const string TenantId = "paybridge.tenant.id";
        public const string IdempotencyKey = "paybridge.idempotency.key";
        public const string Provider = "paybridge.provider";
        public const string ProviderTxnId = "paybridge.provider.txn_id";
        public const string EventType = "paybridge.event.type";
        public const string FraudApproved = "paybridge.fraud.approved";
        public const string FraudRiskScore = "paybridge.fraud.risk_score";
    }

    public static class Metrics
    {
        public const string PaymentsCreated = "paybridge.payments.created";
        public const string PaymentsCompleted = "paybridge.payments.completed";
        public const string PaymentsFailed = "paybridge.payments.failed";
        public const string PaymentLatency = "paybridge.payment.duration";
        public const string FraudChecks = "paybridge.fraud.checks";
        public const string ProviderCalls = "paybridge.provider.calls";
        public const string ProviderCallDuration = "paybridge.provider.duration";
        public const string WebhooksReceived = "paybridge.webhooks.received";
        public const string EventsPublished = "paybridge.events.published";
        public const string EventsConsumed = "paybridge.events.consumed";
        public const string IdempotencyHits = "paybridge.idempotency.hits";
        public const string KillSwitchRejections = "paybridge.killswitch.rejections";
    }
}
