using System.Diagnostics;
using Ledger.Application.Integrity;

namespace Ledger.Infrastructure.Observability;

internal sealed class IntegrityTelemetry(
    TelemetrySources sources,
    LedgerMeters meters,
    IntegritySuccessTracker tracker,
    TimeProvider timeProvider) : IIntegrityTelemetry
{
    public IIntegrityRunTelemetry BeginRun(IntegrityMode mode)
    {
        var activity = sources.Source.StartActivity(SpanNames.IntegrityCheck, ActivityKind.Internal);
        activity?.SetTag(SpanAttributes.IntegrityMode, mode.MetricLabel());

        return new IntegrityRunTelemetry(meters, tracker, timeProvider, activity, mode);
    }
}
