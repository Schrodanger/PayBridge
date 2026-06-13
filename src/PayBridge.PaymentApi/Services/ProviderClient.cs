using System.Diagnostics;
using System.Net.Http.Json;
using PayBridge.PaymentApi.Observability;
using PayBridge.Shared.Domain;
using PayBridge.Shared.Observability;

namespace PayBridge.PaymentApi.Services;

public record ProviderSubmissionResult(bool Accepted, string? ProviderTransactionId, string? Reason);

public interface IProviderClient
{
    Task<ProviderSubmissionResult> SubmitAsync(Payment payment, CancellationToken ct);
}

/// <summary>
/// Talks to the upstream payment provider over HTTP. Resilience policies (retry / circuit
/// breaker / timeout) are configured on the HttpClient via Microsoft.Extensions.Http.Resilience
/// in Program.cs — keep this class focused on shaping the request / parsing the response.
/// </summary>
public sealed class HttpProviderClient : IProviderClient
{
    private readonly HttpClient _http;
    private readonly PaymentMetrics _metrics;
    private readonly ActivitySource _activity;
    private readonly ILogger<HttpProviderClient> _logger;

    public HttpProviderClient(
        HttpClient http,
        PaymentMetrics metrics,
        ActivitySource activity,
        ILogger<HttpProviderClient> logger)
    {
        _http = http;
        _metrics = metrics;
        _activity = activity;
        _logger = logger;
    }

    public async Task<ProviderSubmissionResult> SubmitAsync(Payment payment, CancellationToken ct)
    {
        using var activity = _activity.StartActivity("provider.submit");
        activity?.SetTag(Telemetry.TraceTags.PaymentId, payment.Id);
        activity?.SetTag(Telemetry.TraceTags.Provider, "stub");

        var sw = Stopwatch.StartNew();
        try
        {
            // Pass our payment id in the request so the provider stub can echo it back via webhook,
            // which is how the webhook receiver links the callback to the original trace.
            var resp = await _http.PostAsJsonAsync("/api/charge", new
            {
                paybridgePaymentId = payment.Id,
                amount = payment.Amount,
                currency = payment.Currency,
                method = payment.Method.ToString()
            }, ct);

            var body = await resp.Content.ReadFromJsonAsync<ProviderSubmissionResult>(cancellationToken: ct);
            _metrics.ProviderCall("stub", resp.IsSuccessStatusCode ? "ok" : "error");

            if (body is null)
            {
                return new ProviderSubmissionResult(false, null, "provider returned empty body");
            }

            activity?.SetTag(Telemetry.TraceTags.ProviderTxnId, body.ProviderTransactionId);
            return body;
        }
        catch (Exception ex)
        {
            _metrics.ProviderCall("stub", "exception");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Provider submission failed for payment {PaymentId}", payment.Id);
            throw;
        }
        finally
        {
            _metrics.ProviderCallDuration("stub", sw.Elapsed.TotalMilliseconds);
        }
    }
}
