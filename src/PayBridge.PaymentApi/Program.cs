using MassTransit;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using PayBridge.Contracts.Fraud;
using PayBridge.PaymentApi.Endpoints;
using PayBridge.PaymentApi.Observability;
using PayBridge.PaymentApi.Persistence;
using PayBridge.PaymentApi.Services;
using PayBridge.Shared.Observability;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
const string ServiceName = "paybridge-payment-api";

builder.Host.UsePayBridgeSerilog(ServiceName);

// ---- Web / Swagger ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Accept enums as strings ("CreditCard") rather than integers in request bodies.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// ---- Database ----
var dbConn = builder.Configuration.GetConnectionString("Postgres")
             ?? "Host=postgres;Database=paybridge;Username=paybridge;Password=paybridge";
builder.Services.AddDbContext<PaymentDbContext>(opt => opt.UseNpgsql(dbConn));

// ---- Redis (idempotency + kill switch) ----
var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "redis:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
builder.Services.AddSingleton<IKillSwitch, RedisKillSwitch>();

// ---- gRPC fraud client (+ resilience) ----
var fraudUri = new Uri(builder.Configuration["FraudService:Url"] ?? "http://fraud-service:8080");
builder.Services.AddGrpcClient<FraudDetection.FraudDetectionClient>(o =>
{
    o.Address = fraudUri;
}).AddStandardResilienceHandler(); // retry, timeout, circuit breaker — all observable via metrics

// ---- HTTP provider client (+ resilience) ----
var providerUri = new Uri(builder.Configuration["ProviderService:Url"] ?? "http://provider-stub:8080");
builder.Services.AddHttpClient<IProviderClient, HttpProviderClient>(c =>
{
    c.BaseAddress = providerUri;
    c.Timeout = TimeSpan.FromSeconds(10);
}).AddStandardResilienceHandler();

// ---- Messaging (RabbitMQ via MassTransit) ----
var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "rabbitmq";
var rabbitUser = builder.Configuration["RabbitMq:User"] ?? "paybridge";
var rabbitPass = builder.Configuration["RabbitMq:Pass"] ?? "paybridge";
builder.Services.AddMassTransit(cfg =>
{
    cfg.SetKebabCaseEndpointNameFormatter();
    cfg.UsingRabbitMq((ctx, rmq) =>
    {
        rmq.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });
    });
});
builder.Services.AddScoped<IPaymentEventPublisher, MassTransitPaymentEventPublisher>();

// ---- Domain services ----
builder.Services.AddScoped<IFraudClient, GrpcFraudClient>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddSingleton<PaymentMetrics>();

// ---- Observability ----
builder.Services.AddPayBridgeObservability(
    builder.Configuration,
    ServiceName,
    configureTracing: t =>
    {
        t.AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true);
        t.AddRedisInstrumentation();
    });

// ---- Health checks ----
builder.Services.AddHealthChecks()
    .AddNpgSql(dbConn, name: "postgres", tags: new[] { "critical" })
    .AddRabbitMQ(
        rabbitConnectionString: $"amqp://{rabbitUser}:{rabbitPass}@{rabbitHost}:5672",
        name: "rabbitmq",
        tags: new[] { "critical" })
    .AddRedis(redisConn, name: "redis", tags: new[] { "cache" });

var app = builder.Build();

// Apply migrations / schema on boot. EnsureCreated keeps the example simple; real prod would
// use proper EF migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await db.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPaymentEndpoints();
app.MapWebhookEndpoints();
app.MapPayBridgeHealthEndpoints();

app.Run();

// Make Program accessible to WebApplicationFactory<Program> in tests.
public partial class Program { }
