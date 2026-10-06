using System.Collections.Concurrent;
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

internal sealed class IntegritySuccessTracker
{
    private readonly ConcurrentDictionary<IntegrityMode, long> _lastSuccessMilliseconds = new();

    public IReadOnlyList<KeyValuePair<IntegrityMode, double>> Snapshot()
    {
        return
        [
            .. _lastSuccessMilliseconds.Select(pair =>
                new KeyValuePair<IntegrityMode, double>(pair.Key, pair.Value / 1000d))
        ];
    }

    public void Record(IntegrityMode mode, DateTimeOffset at)
    {
        _lastSuccessMilliseconds[mode] = at.ToUnixTimeMilliseconds();
    }
}
