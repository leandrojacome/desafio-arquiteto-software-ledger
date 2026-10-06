using System.Diagnostics.CodeAnalysis;

namespace Ledger.Application.Integrity;

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let an unset check pass for a valid one; the default must stay outside the defined values.")]
public enum IntegrityCheck
{
    HeadBalance = 1,
    HeadVersion = 2,
    HeadLastEntry = 3,
    HeadFloor = 4,
    ChainDrift = 5,
    ChainGap = 6,
    ChainNonMonotonic = 7,
    SumBalance = 8
}

public static class IntegrityCheckExtensions
{
    public const string BalanceMismatchKind = "balance_mismatch";
    public const string ChainBrokenKind = "chain_broken";

    public static string AuditName(this IntegrityCheck check)
    {
        return check switch
        {
            IntegrityCheck.HeadBalance => "HEAD_BALANCE",
            IntegrityCheck.HeadVersion => "HEAD_VERSION",
            IntegrityCheck.HeadLastEntry => "HEAD_LAST_ENTRY",
            IntegrityCheck.HeadFloor => "HEAD_FLOOR",
            IntegrityCheck.ChainDrift => "CHAIN_DRIFT",
            IntegrityCheck.ChainGap => "CHAIN_GAP",
            IntegrityCheck.ChainNonMonotonic => "CHAIN_NON_MONOTONIC",
            IntegrityCheck.SumBalance => "SUM_BALANCE",
            _ => throw new ArgumentOutOfRangeException(nameof(check), check, "Unknown integrity check.")
        };
    }

    public static string MetricKind(this IntegrityCheck check)
    {
        return check switch
        {
            IntegrityCheck.HeadBalance or IntegrityCheck.HeadVersion or IntegrityCheck.HeadLastEntry
                or IntegrityCheck.HeadFloor or IntegrityCheck.SumBalance => BalanceMismatchKind,
            IntegrityCheck.ChainDrift or IntegrityCheck.ChainGap or IntegrityCheck.ChainNonMonotonic =>
                ChainBrokenKind,
            _ => throw new ArgumentOutOfRangeException(nameof(check), check, "Unknown integrity check.")
        };
    }
}
