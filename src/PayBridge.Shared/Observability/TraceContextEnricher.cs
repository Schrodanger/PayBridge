using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace PayBridge.Shared.Observability;

/// <summary>
/// Stamps every log event with the active trace_id / span_id so logs and traces
/// can be cross-referenced in the backend (Grafana, Aspire, etc).
/// </summary>
public sealed class TraceContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("trace_id", activity.TraceId.ToString()));
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("span_id", activity.SpanId.ToString()));
    }
}
