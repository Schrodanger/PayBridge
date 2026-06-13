using PayBridge.FraudService.Services;
using PayBridge.Shared.Observability;

var builder = WebApplication.CreateBuilder(args);
const string ServiceName = "paybridge-fraud-service";

builder.Host.UsePayBridgeSerilog(ServiceName);

// gRPC binds to HTTP/2; Kestrel must allow plaintext HTTP/2 inside the cluster.
builder.WebHost.ConfigureKestrel(opt =>
{
    var port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "8080");
    opt.ListenAnyIP(port, lo => lo.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});

builder.Services.AddGrpc();
builder.Services.AddPayBridgeObservability(builder.Configuration, ServiceName);

var app = builder.Build();

app.MapGrpcService<FraudDetectionService>();
app.MapGet("/", () => "PayBridge Fraud Detection (gRPC). Use a gRPC client.");

app.Run();

public partial class Program { }
