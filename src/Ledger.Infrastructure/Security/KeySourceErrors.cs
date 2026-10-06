using System.Diagnostics.CodeAnalysis;

namespace Ledger.Infrastructure.Security;

internal enum KeyMaterialProblem
{
    BadLength = 1,
    IdenticalKeys = 2,
    BadBase64 = 3,
    NoActiveSet = 4,
    RoundTripFailed = 5,
    DuplicateVersion = 6,
    VersionChanged = 7
}

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

[SuppressMessage("Design", "CA1032",
    Justification = "The problem and the message are required, so the standard parameterless constructors would build an invalid exception.")]
internal sealed class KeyMaterialRejectedException : InvalidOperationException
{
    public KeyMaterialRejectedException(KeyMaterialProblem problem, string message)
        : base(message)
    {
        Problem = problem;
    }

    public KeyMaterialProblem Problem { get; }
}

internal sealed class KeySourceUnavailableException : InvalidOperationException
{
    public KeySourceUnavailableException()
        : base("The key source is unavailable.")
    {
    }

    public KeySourceUnavailableException(string message)
        : base(message)
    {
    }

    public KeySourceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
