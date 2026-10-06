namespace Ledger.Infrastructure.Security;

internal static class KeyMaterialProblemExtensions
{
    public static string ToReason(this KeyMaterialProblem problem)
    {
        return problem switch
        {
            KeyMaterialProblem.BadLength => "bad_length",
            KeyMaterialProblem.IdenticalKeys => "identical_keys",
            KeyMaterialProblem.BadBase64 => "bad_base64",
            KeyMaterialProblem.NoActiveSet => "no_active_set",
            KeyMaterialProblem.RoundTripFailed => "round_trip_failed",
            KeyMaterialProblem.DuplicateVersion => "duplicate_version",
            KeyMaterialProblem.VersionChanged => "version_changed",
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Unknown key material problem.")
        };
    }
}
