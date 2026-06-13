using PayBridge.ProviderStub.Contracts;
using PayBridge.ProviderStub.Services;
using PayBridge.Shared.Observability;

var builder = WebApplication.CreateBuilder(args);
const string ServiceName = "paybridge-provider-stub";

builder.Host.UsePayBridgeSerilog(ServiceName);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient("paymentApi");
builder.Services.AddSingleton<WebhookDispatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WebhookDispatcher>());

builder.Services.AddPayBridgeObservability(builder.Configuration, ServiceName);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/api/charge", (ChargeRequest req, WebhookDispatcher dispatcher, ILogger<Program> log) =>
{
    var rng = Random.Shared;
    // Simulate occasional outright rejection at the synchronous-accept stage.
    if (rng.NextDouble() < 0.05)
    {
        return Results.Ok(new ChargeResponse(false, null, "provider_declined_immediate"));
    }

    var txn = $"prov_{Guid.NewGuid():N}".Substring(0, 24);

    // 90% async success, 10% async failure — webhook fires in 200-800ms.
    var success = rng.NextDouble() > 0.10;
    var delay = TimeSpan.FromMilliseconds(rng.Next(200, 800));

    dispatcher.Enqueue(new WebhookCallback(
        ProviderTransactionId: txn,
        Status: success ? "SUCCESS" : "FAILED",
        Timestamp: DateTime.UtcNow,
        Metadata: new Dictionary<string, string>
        {
            ["paybridge_payment_id"] = req.PaybridgePaymentId.ToString()
        }), delay);

    log.LogInformation(
        "Accepted charge for {PaymentId} -> txn {Txn}; webhook in {Delay}ms (success={Success})",
        req.PaybridgePaymentId, txn, delay.TotalMilliseconds, success);

    return Results.Ok(new ChargeResponse(true, txn, null));
}).WithName("Charge");

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }
