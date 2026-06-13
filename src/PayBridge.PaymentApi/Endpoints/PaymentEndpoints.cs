using Microsoft.AspNetCore.Mvc;
using PayBridge.PaymentApi.Contracts;
using PayBridge.PaymentApi.Services;

namespace PayBridge.PaymentApi.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/payments").WithTags("Payments");

        grp.MapPost("/", async (
            [FromHeader(Name = "X-Tenant-Id")] string? tenantHeader,
            [FromBody] CreatePaymentRequest request,
            PaymentService svc,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.MerchantId)
                || string.IsNullOrWhiteSpace(request.IdempotencyKey)
                || string.IsNullOrWhiteSpace(request.Currency)
                || request.Amount <= 0)
            {
                return Results.BadRequest(new { error = "merchant_id, idempotency_key, currency and positive amount are required" });
            }

            var tenantId = string.IsNullOrWhiteSpace(tenantHeader) ? "default" : tenantHeader;

            try
            {
                var (resp, created) = await svc.CreateAsync(request, tenantId, ct);
                return created
                    ? Results.Created($"/api/payments/{resp.Id}", resp)
                    : Results.Ok(resp);
            }
            catch (PaymentRejectedException ex)
            {
                return Results.Json(
                    new { error = "payments_disabled", detail = ex.Message },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        grp.MapGet("/{id:guid}", async (
            Guid id,
            Persistence.PaymentDbContext db,
            CancellationToken ct) =>
        {
            var p = await db.Payments.FindAsync(new object?[] { id }, ct);
            return p is null ? Results.NotFound() : Results.Ok(PaymentResponse.FromEntity(p));
        });

        return app;
    }
}
