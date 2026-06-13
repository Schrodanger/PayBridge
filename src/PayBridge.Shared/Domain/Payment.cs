namespace PayBridge.Shared.Domain;

public class Payment
{
    public Guid Id { get; set; }
    public string MerchantId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Trace correlation: trace id captured at the moment the payment was created.
    // Lets the webhook handler / consumer link spans back to the original payment trace.
    public string? OriginatingTraceId { get; set; }
}
