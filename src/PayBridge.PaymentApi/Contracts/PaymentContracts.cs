using PayBridge.Shared.Domain;

namespace PayBridge.PaymentApi.Contracts;

public record CreatePaymentRequest(
    string MerchantId,
    string IdempotencyKey,
    decimal Amount,
    string Currency,
    string CustomerEmail,
    PaymentMethod Method,
    Dictionary<string, string>? Metadata
);

public record PaymentResponse(
    Guid Id,
    string MerchantId,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    string? ProviderTransactionId,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? CompletedAt
)
{
    public static PaymentResponse FromEntity(Payment p) => new(
        p.Id,
        p.MerchantId,
        p.Amount,
        p.Currency,
        p.Status,
        p.ProviderTransactionId,
        p.FailureReason,
        p.CreatedAt,
        p.CompletedAt
    );
}

public record ProviderWebhookCallback(
    string ProviderTransactionId,
    string Status,
    DateTime Timestamp,
    Dictionary<string, string>? Metadata
);
