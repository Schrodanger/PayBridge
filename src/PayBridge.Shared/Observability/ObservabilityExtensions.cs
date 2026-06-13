using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace PayBridge.Shared.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Wires Serilog as the logging provider. Logs are written as compact JSON to stdout
    /// and enriched with the active trace_id / span_id so logs can be joined to traces.
    /// </summary>
    public static IHostBuilder UsePayBridgeSerilog(this IHostBuilder hostBuilder, string serviceName)
    {
        return hostBuilder.UseSerilog((ctx, services, cfg) =>
        {
            cfg
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("MassTransit", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithProperty("service.name", serviceName)
                .Enrich.With(new TraceContextEnricher())
                .WriteTo.Console(new CompactJsonFormatter())
                .ReadFrom.Configuration(ctx.Configuration);
        });
    }

    /// <summary>
    /// Adds OpenTelemetry tracing + metrics + log signal export over OTLP.
    /// Each service supplies extra wiring (gRPC client, EF, Redis, etc.) via the configure callbacks.
    /// </summary>
    public static IServiceCollection AddPayBridgeObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        var otlpEndpoint = configuration["Otel:Endpoint"] ?? "http://otel-collector:4317";
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: "1.0.0")
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("deployment.environment", configuration["ASPNETCORE_ENVIRONMENT"] ?? "Development")
            });

        services.AddSingleton(new ActivitySource(Telemetry.ActivitySourceName));

        services.AddOpenTelemetry()
            .ConfigureResource(rb => rb.AddService(serviceName: serviceName, serviceVersion: "1.0.0"))
            .WithTracing(t =>
            {
                t.AddSource(Telemetry.ActivitySourceName)
                 .AddSource("MassTransit")
                 .SetResourceBuilder(resourceBuilder)
                 .AddAspNetCoreInstrumentation(opts =>
                 {
                     opts.RecordException = true;
                     opts.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                 })
                 .AddHttpClientInstrumentation(opts =>
                 {
                     opts.RecordException = true;
                 })
                 .AddGrpcClientInstrumentation();

                configureTracing?.Invoke(t);

                t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(m =>
            {
                m.AddMeter(Telemetry.MeterName)
                 .SetResourceBuilder(resourceBuilder)
                 .AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation()
                 .AddRuntimeInstrumentation();

                configureMetrics?.Invoke(m);

                m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            });

        return services;
    }
}
