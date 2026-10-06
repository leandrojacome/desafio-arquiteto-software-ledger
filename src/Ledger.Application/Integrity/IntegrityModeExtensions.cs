namespace Ledger.Application.Integrity;

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
