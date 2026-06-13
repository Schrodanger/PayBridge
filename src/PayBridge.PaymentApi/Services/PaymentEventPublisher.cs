using MassTransit;
using PayBridge.PaymentApi.Observability;
using PayBridge.Shared.Messaging;

namespace PayBridge.PaymentApi.Services;

public interface IPaymentEventPublisher
{
    Task PublishAsync(PaymentEvent evt, CancellationToken ct);
}

public sealed class MassTransitPaymentEventPublisher : IPaymentEventPublisher
{
    private readonly IPublishEndpoint _publish;
    private readonly PaymentMetrics _metrics;
    private readonly ILogger<MassTransitPaymentEventPublisher> _logger;

    public MassTransitPaymentEventPublisher(
        IPublishEndpoint publish,
        PaymentMetrics metrics,
        ILogger<MassTransitPaymentEventPublisher> logger)
    {
        _publish = publish;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task PublishAsync(PaymentEvent evt, CancellationToken ct)
    {
        try
        {
            await _publish.Publish(evt, ct);
            _metrics.EventPublished(evt.EventType);
        }
        catch (Exception ex)
        {
            // We deliberately do NOT throw — publish failures shouldn't fail the HTTP request.
            // The DB has the canonical state; an outbox / replay job would re-publish missed events.
            _logger.LogError(ex, "Failed to publish {EventType} for payment {PaymentId}", evt.EventType, evt.PaymentId);
        }
    }
}
