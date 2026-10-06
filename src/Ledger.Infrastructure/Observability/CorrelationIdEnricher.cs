using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Infrastructure.Observability;

internal sealed class CorrelationIdEnricher : ILogEventEnricher
{
    public const string CorrelationIdProperty = "CorrelationId";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        if (logEvent.Properties.ContainsKey(CorrelationIdProperty))
        {
            return;
        }

        if (ActivityTags.Find(Activity.Current, ActivityTags.CorrelationId) is { } correlationId)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(CorrelationIdProperty, correlationId));
        }
    }
}
