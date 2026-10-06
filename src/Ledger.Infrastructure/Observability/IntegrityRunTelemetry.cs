using System.Diagnostics;
using Ledger.Application.Integrity;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Observability;

internal sealed class IntegrityRunTelemetry(
    LedgerMeters meters,
    IntegritySuccessTracker tracker,
    TimeProvider timeProvider,
    Activity? activity,
    IntegrityMode mode) : IIntegrityRunTelemetry
{
    private readonly long _startedAt = timeProvider.GetTimestamp();
    private readonly string _modeLabel = mode.MetricLabel();
    private bool _resultRecorded;
    private bool _disposed;

    public void Violation(IntegrityCheck check)
    {
        if (LabelTable<IntegrityViolationKind>.TryParse(check.MetricKind(), out var kind))
        {
            meters.IntegrityViolations.Add(1, new TagList { { TagKeys.Kind, kind.Label() } });
        }
    }

    public void AccountsChecked(long count)
    {
        activity?.SetTag(SpanAttributes.IntegrityAccountsChecked, count);
    }

    public void Completed(bool clean)
    {
        if (!TryRecordResult(clean ? IntegrityResult.Ok : IntegrityResult.Violation))
        {
            return;
        }

        if (clean)
        {
            tracker.Record(mode, timeProvider.GetUtcNow());
        }
    }

    public void Failed()
    {
        if (TryRecordResult(IntegrityResult.Error))
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_resultRecorded)
        {
            var elapsed = timeProvider.GetElapsedTime(_startedAt);

            meters.IntegrityDuration.Record(elapsed.TotalSeconds, new TagList { { TagKeys.Mode, _modeLabel } });
        }

        activity?.Dispose();
    }

    private bool TryRecordResult(IntegrityResult result)
    {
        if (_resultRecorded)
        {
            return false;
        }

        _resultRecorded = true;
        meters.IntegrityRuns.Add(1, new TagList { { TagKeys.Mode, _modeLabel }, { TagKeys.Result, result.Label() } });
        activity?.SetTag(SpanAttributes.Outcome, result.Label());

        return true;
    }
}
