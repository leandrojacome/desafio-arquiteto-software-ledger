namespace Ledger.Application.Integrity;

public sealed record RunIntegrityCheckCommand
{
    public RunIntegrityCheckCommand(
        IntegrityMode mode,
        TimeSpan recentInterval,
        TimeSpan overlap,
        TimeSpan fullLookback,
        TimeSpan chainSlice,
        int headBatchSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(recentInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(overlap, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fullLookback, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(chainSlice, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(headBatchSize, 1);

        Mode = mode;
        RecentInterval = recentInterval;
        Overlap = overlap;
        FullLookback = fullLookback;
        ChainSlice = chainSlice;
        HeadBatchSize = headBatchSize;
    }

    public IntegrityMode Mode { get; }

    public TimeSpan RecentInterval { get; }

    public TimeSpan Overlap { get; }

    public TimeSpan FullLookback { get; }

    public TimeSpan ChainSlice { get; }

    public int HeadBatchSize { get; }
}

public sealed record IntegrityRunSummary(
    IntegrityMode Mode,
    bool Skipped,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    long AccountsChecked,
    long EntriesChecked,
    int Violations,
    bool Partial = false)
{
    public static IntegrityRunSummary SkippedRun(IntegrityMode mode) =>
        new(mode, true, default, default, 0, 0, 0);
}

public sealed record IntegrityRunRecord(
    DateTimeOffset RecordedAt,
    string Outcome,
    IntegrityMode Mode,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd);
