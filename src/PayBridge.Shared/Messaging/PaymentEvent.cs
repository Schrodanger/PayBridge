namespace PayBridge.Shared.Messaging;

public record PaymentEvent(
    Guid PaymentId,
    string MerchantId,
    string TenantId,
    string EventType, // "PaymentInitiated", "PaymentCompleted", "PaymentFailed"
    decimal Amount,
    string Currency,
    string? ProviderTransactionId,
    string? FailureReason,
    DateTime Timestamp
);

public static class PaymentEventTypes
{
    public const string Initiated = "PaymentInitiated";
    public const string Completed = "PaymentCompleted";
    public const string Failed = "PaymentFailed";
}
