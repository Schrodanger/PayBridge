using MassTransit;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using PayBridge.SettlementConsumer.Consumers;
using PayBridge.SettlementConsumer.Persistence;
using PayBridge.Shared.Observability;
using Serilog;

const string ServiceName = "paybridge-settlement-consumer";

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
Log.Logger = new Serilog.LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithProperty("service.name", ServiceName)
    .Enrich.With(new PayBridge.Shared.Observability.TraceContextEnricher())
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
    .CreateLogger();
builder.Logging.AddSerilog(Log.Logger);

var dbConn = builder.Configuration.GetConnectionString("Postgres")
             ?? "Host=postgres;Database=paybridge;Username=paybridge;Password=paybridge";
builder.Services.AddDbContext<SettlementDbContext>(opt => opt.UseNpgsql(dbConn));

var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "rabbitmq";
var rabbitUser = builder.Configuration["RabbitMq:User"] ?? "paybridge";
var rabbitPass = builder.Configuration["RabbitMq:Pass"] ?? "paybridge";

builder.Services.AddMassTransit(cfg =>
{
    cfg.SetKebabCaseEndpointNameFormatter();
    cfg.AddConsumer<PaymentEventConsumer>();
    cfg.UsingRabbitMq((ctx, rmq) =>
    {
        rmq.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });

        rmq.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
        rmq.ConfigureEndpoints(ctx);
    });
});

builder.Services.AddPayBridgeObservability(builder.Configuration, ServiceName,
    configureTracing: t => t.AddEntityFrameworkCoreInstrumentation());

var host = builder.Build();

// Schema bootstrap is owned by the Payment API (it creates both `payments` and `settlements`
// tables). The consumer just reads/writes; no DDL here.

await host.RunAsync();

public partial class Program { }
