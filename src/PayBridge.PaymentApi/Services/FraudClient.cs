using System.Diagnostics;
using PayBridge.Contracts.Fraud;
using PayBridge.PaymentApi.Observability;
using PayBridge.Shared.Domain;
using PayBridge.Shared.Observability;

namespace PayBridge.PaymentApi.Services;

public record FraudResult(bool Approved, double RiskScore, string Reason);

public interface IFraudClient
{
    Task<FraudResult> CheckAsync(Payment payment, string customerEmail, CancellationToken ct);
}

public sealed class GrpcFraudClient : IFraudClient
{
    private readonly FraudDetection.FraudDetectionClient _client;
    private readonly PaymentMetrics _metrics;
    private readonly ActivitySource _activity;
    private readonly ILogger<GrpcFraudClient> _logger;

    public GrpcFraudClient(
        FraudDetection.FraudDetectionClient client,
        PaymentMetrics metrics,
        ActivitySource activity,
        ILogger<GrpcFraudClient> logger)
    {
        _client = client;
        _metrics = metrics;
        _activity = activity;
        _logger = logger;
    }

    public async Task<FraudResult> CheckAsync(Payment payment, string customerEmail, CancellationToken ct)
    {
        using var activity = _activity.StartActivity("fraud.check");
        activity?.SetTag(Telemetry.TraceTags.PaymentId, payment.Id);
        activity?.SetTag(Telemetry.TraceTags.MerchantId, payment.MerchantId);

        try
        {
            var resp = await _client.CheckTransactionAsync(new FraudCheckRequest
            {
                PaymentId = payment.Id.ToString(),
                MerchantId = payment.MerchantId,
                Amount = (double)payment.Amount,
                Currency = payment.Currency,
                CustomerEmail = customerEmail,
                PaymentMethod = payment.Method.ToString()
            }, cancellationToken: ct);

            activity?.SetTag(Telemetry.TraceTags.FraudApproved, resp.Approved);
            activity?.SetTag(Telemetry.TraceTags.FraudRiskScore, resp.RiskScore);

            _metrics.FraudCheck(resp.Approved ? "approved" : "rejected");
            return new FraudResult(resp.Approved, resp.RiskScore, resp.Reason);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _metrics.FraudCheck("error");
            _logger.LogError(ex, "Fraud check failed for payment {PaymentId}", payment.Id);
            throw;
        }
    }
}
