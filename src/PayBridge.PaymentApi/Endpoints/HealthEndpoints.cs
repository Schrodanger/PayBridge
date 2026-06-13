using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PayBridge.PaymentApi.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapPayBridgeHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness — only fails if the process is unhealthy. K8s uses this to decide to restart the pod.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });

        // Readiness — fails when *critical* dependencies are down (db, broker). Removes the pod
        // from the LB but does NOT restart. Non-critical deps (cache, fraud) downgrade to "degraded".
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains("critical"),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });

        // Full status — for dashboards / operators.
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            },
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });

        return app;
    }
}
