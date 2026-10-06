using System.Diagnostics;
using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Observability;

internal sealed class EntryTelemetry(
    TelemetrySources sources,
    LedgerMeters meters,
    ClientLabels clients,
    TimeProvider timeProvider) : IEntryTelemetry
{
    public IEntryOperation Begin(string operation)
    {
        EntryTypeLabel? type = LabelTable<EntryTypeLabel>.TryParse(operation, out var parsed) ? parsed : null;
        var client = clients.Resolve(ActivityTags.Find(Activity.Current, ActivityTags.ClientId));
        var activity = sources.Source.StartActivity(SpanNames.RecordEntry, ActivityKind.Internal);

        if (type is { } label)
        {
            activity?.SetTag(SpanAttributes.EntryType, label.Label());
        }

        return new EntryOperation(meters, timeProvider, activity, type, client);
    }
}
