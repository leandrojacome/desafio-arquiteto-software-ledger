using System.ComponentModel.DataAnnotations;

namespace Ledger.Worker;

internal sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public WorkerFailureBackoffOptions FailureBackoff { get; init; } = new();
}

internal sealed class WorkerFailureBackoffOptions
{
    [Range(1, 60)] public int MinSeconds { get; init; } = 1;

    [Range(1, 60)] public int MaxSeconds { get; init; } = 30;
}
