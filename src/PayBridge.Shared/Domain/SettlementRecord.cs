namespace PayBridge.Shared.Domain;

public class SettlementRecord
{
    public long Id { get; set; }
    public Guid PaymentId { get; set; }
    public string MerchantId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentStatus FinalStatus { get; set; }
    public string? ProviderTransactionId { get; set; }
    public DateTime EventTimestamp { get; set; }
    public DateTime PersistedAt { get; set; }
}
