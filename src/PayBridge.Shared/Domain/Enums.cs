namespace PayBridge.Shared.Domain;

public enum PaymentMethod
{
    CreditCard,
    DebitCard,
    BankTransfer,
    Wallet
}

public enum PaymentStatus
{
    Created,
    FraudChecking,
    Submitted,
    Completed,
    Failed,
    Refunded
}
