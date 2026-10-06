namespace Ledger.Application.Accounts;

public sealed record RewrapPassResult(int Rewrapped, int Failed, bool Converged, bool Skipped = false)
{
    public static RewrapPassResult SkippedPass { get; } = new(0, 0, false, true);
}
