using System.Diagnostics.CodeAnalysis;

namespace Ledger.Application.Integrity;

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let an unset mode pass for a valid one; the default must stay outside the defined values.")]
public enum IntegrityMode
{
    Recent = 1,
    Full = 2
}

public static class IntegrityModeExtensions
{
    public static string AuditText(this IntegrityMode mode)
    {
        return mode switch
        {
            IntegrityMode.Recent => "RECENT",
            IntegrityMode.Full => "FULL",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown integrity mode.")
        };
    }

    public static string MetricLabel(this IntegrityMode mode)
    {
        return mode switch
        {
            IntegrityMode.Recent => "incremental",
            IntegrityMode.Full => "full",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown integrity mode.")
        };
    }

    public static int LockKey(this IntegrityMode mode)
    {
        return mode switch
        {
            IntegrityMode.Recent => 1,
            IntegrityMode.Full => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown integrity mode.")
        };
    }
}
