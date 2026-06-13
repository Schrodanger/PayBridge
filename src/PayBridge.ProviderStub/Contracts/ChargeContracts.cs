using PayBridge.ProviderStub.Services;

namespace PayBridge.ProviderStub.Contracts;

public record ChargeRequest(Guid PaybridgePaymentId, decimal Amount, string Currency, string Method);

public record ChargeResponse(bool Accepted, string? ProviderTransactionId, string? Reason);

public record WebhookCallback(
    string ProviderTransactionId,
    string Status,
    DateTime Timestamp,
    Dictionary<string, string> Metadata);
