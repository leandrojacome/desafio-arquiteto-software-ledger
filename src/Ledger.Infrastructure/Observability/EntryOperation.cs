using System.Diagnostics;
using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Observability;

internal sealed class EntryOperation(
    LedgerMeters meters,
    TimeProvider timeProvider,
    Activity? activity,
    EntryTypeLabel? initialType,
    string client) : IEntryOperation
{
    private readonly long _startedAt = timeProvider.GetTimestamp();
    private EntryTypeLabel? _type = initialType;
    private EntryResultLabel _result = EntryResultLabel.Failed;
    private bool _disposed;

    public void Recorded(string type)
    {
        _result = EntryResultLabel.Recorded;

        if (!LabelTable<EntryTypeLabel>.TryParse(type, out var parsed))
        {
            return;
        }

        _type = parsed;
        activity?.SetTag(SpanAttributes.EntryType, parsed.Label());
        meters.EntriesRecorded.Add(1, new TagList { { TagKeys.Type, parsed.Label() }, { TagKeys.Client, client } });
    }

    public void Replayed()
    {
        _result = EntryResultLabel.Replayed;
        meters.IdempotencyReplays.Add(1, new TagList { { TagKeys.Client, client } });
    }

    public void Rejected(string reason)
    {
        _result = EntryResultLabel.Rejected;

        if (LabelTable<RejectionReason>.TryParse(reason, out var parsed))
        {
            CountRejection(parsed);
        }
    }

    public void IdempotencyConflict()
    {
        _result = EntryResultLabel.Rejected;
        meters.IdempotencyConflicts.Add(1, new TagList { { TagKeys.Client, client } });
        CountRejection(RejectionReason.IdempotencyConflict);
    }

    public void RecordedAtCorrected()
    {
        meters.RecordedAtCorrections.Add(1, new TagList { { TagKeys.Client, client } });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_type is { } label)
        {
            var elapsed = timeProvider.GetElapsedTime(_startedAt);

            meters.EntryDuration.Record(
                elapsed.TotalSeconds,
                new TagList { { TagKeys.Type, label.Label() }, { TagKeys.Outcome, _result.Label() } });
        }

        if (activity is null)
        {
            return;
        }

        activity.SetTag(SpanAttributes.Outcome, _result.Label());
        activity.SetTag(SpanAttributes.IdempotentReplay, _result == EntryResultLabel.Replayed);

        if (_result == EntryResultLabel.Failed)
        {
            activity.SetStatus(ActivityStatusCode.Error);
        }

        activity.Dispose();
    }

    private void CountRejection(RejectionReason reason)
    {
        meters.EntriesRejected.Add(1, new TagList { { TagKeys.Reason, reason.Label() }, { TagKeys.Client, client } });
    }
}
