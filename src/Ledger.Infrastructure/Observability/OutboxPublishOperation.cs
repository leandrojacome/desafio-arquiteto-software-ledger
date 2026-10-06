using System.Diagnostics;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Observability;

internal sealed class OutboxPublishOperation(
    LedgerMeters meters,
    TimeProvider timeProvider,
    Activity? activity,
    Activity? previous) : IOutboxPublishOperation
{
    private readonly long _startedAt = timeProvider.GetTimestamp();
    private bool _disposed;

    public void Confirmed()
    {
        meters.OutboxPublishDuration.Record(timeProvider.GetElapsedTime(_startedAt).TotalSeconds);
        activity?.SetTag(SpanAttributes.Outcome, PublishOutcome.Confirmed.Label());
    }

    public void Failed(PublishFailureReason reason)
    {
        activity?.SetTag(SpanAttributes.Outcome, PublishOutcome.Failed.Label());
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (activity is null)
        {
            return;
        }

        activity.Dispose();
        Activity.Current = previous;
    }
}
