using System.Collections.Concurrent;
using System.Net.Http.Json;
using PayBridge.ProviderStub.Contracts;

namespace PayBridge.ProviderStub.Services;

/// <summary>
/// Queues a delayed webhook callback to the Payment API so the full async pipeline
/// (REST -> gRPC -> HTTP -> webhook -> queue -> consumer -> SQL) is exercised end-to-end.
/// </summary>
public sealed class WebhookDispatcher : BackgroundService
{
    private readonly ConcurrentQueue<(DateTime Due, WebhookCallback Callback)> _pending = new();
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<WebhookDispatcher> _logger;

    public WebhookDispatcher(IHttpClientFactory httpFactory, IConfiguration config, ILogger<WebhookDispatcher> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public void Enqueue(WebhookCallback cb, TimeSpan delay) =>
        _pending.Enqueue((DateTime.UtcNow + delay, cb));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var paymentApi = _config["PaymentApi:Url"] ?? "http://payment-api:8080";
        var client = _httpFactory.CreateClient("paymentApi");
        client.BaseAddress = new Uri(paymentApi);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_pending.TryPeek(out var head) && DateTime.UtcNow >= head.Due)
                {
                    _pending.TryDequeue(out var dueItem);
                    try
                    {
                        var resp = await client.PostAsJsonAsync("/webhooks/provider", dueItem.Callback, stoppingToken);
                        _logger.LogInformation(
                            "Webhook delivered for txn {Txn}: status={HttpStatus}",
                            dueItem.Callback.ProviderTransactionId, (int)resp.StatusCode);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Webhook delivery failed; dropping (stub does not retry)");
                    }
                }
                else
                {
                    await Task.Delay(150, stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}
