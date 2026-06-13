using Grpc.Core;
using PayBridge.Contracts.Fraud;
using PayBridge.Shared.Observability;

namespace PayBridge.FraudService.Services;

/// <summary>
/// Stub fraud detection: returns a random risk score and approves anything below 0.85.
/// Requests over $10k get extra scrutiny (lower approval rate) to give the dashboards
/// something interesting to look at.
/// </summary>
public class FraudDetectionService : FraudDetection.FraudDetectionBase
{
    private static readonly Random Rng = Random.Shared;
    private readonly ILogger<FraudDetectionService> _logger;

    public FraudDetectionService(ILogger<FraudDetectionService> logger)
    {
        _logger = logger;
    }

    public override async Task<FraudCheckResponse> CheckTransaction(FraudCheckRequest request, ServerCallContext context)
    {
        // Add a bit of latency so traces / histograms look realistic.
        await Task.Delay(Rng.Next(10, 80), context.CancellationToken);

        var baseRisk = Rng.NextDouble() * 0.6; // 0.0 - 0.6
        var highValueBoost = request.Amount > 10_000 ? 0.4 : 0.0;
        var risk = Math.Min(1.0, baseRisk + highValueBoost);
        var approved = risk < 0.85;

        _logger.LogInformation(
            "Fraud check for payment {PaymentId} amount {Amount} -> risk={Risk:F2} approved={Approved}",
            request.PaymentId, request.Amount, risk, approved);

        return new FraudCheckResponse
        {
            Approved = approved,
            RiskScore = risk,
            Reason = approved ? "ok" : (risk >= 0.95 ? "high_risk_pattern" : "elevated_risk")
        };
    }
}
