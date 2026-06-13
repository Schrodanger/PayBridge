using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PayBridge.PaymentApi.Contracts;
using PayBridge.PaymentApi.Services;
using PayBridge.Shared.Observability;

namespace PayBridge.PaymentApi.Endpoints;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        // Trace linking: the webhook arrives as a brand-new HTTP request (and therefore a new
        // server-side span). To stitch it into the original payment trace we add a span link to
        // the originating trace id we stored on the Payment record at creation time.
        app.MapPost("/webhooks/provider", async (
            [FromBody] ProviderWebhookCallback callback,
            PaymentService svc,
            ActivitySource activity,
            CancellationToken ct) =>
        {
            using var span = activity.StartActivity("webhook.provider");
            span?.SetTag(Telemetry.TraceTags.ProviderTxnId, callback.ProviderTransactionId);

            var payment = await svc.ApplyWebhookAsync(callback, ct);
            if (payment is null)
            {
                return Results.NotFound(new { error = "unknown payment or malformed metadata" });
            }

            if (!string.IsNullOrEmpty(payment.OriginatingTraceId))
            {
                span?.SetTag("paybridge.originating_trace_id", payment.OriginatingTraceId);
            }

            return Results.Ok(new { acknowledged = true, status = payment.Status.ToString() });
        }).WithTags("Webhooks");

        return app;
    }
}
