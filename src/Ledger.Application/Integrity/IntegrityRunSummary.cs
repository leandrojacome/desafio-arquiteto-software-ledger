namespace Ledger.Application.Integrity;

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
