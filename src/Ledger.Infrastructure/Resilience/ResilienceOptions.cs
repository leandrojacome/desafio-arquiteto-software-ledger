using System.ComponentModel.DataAnnotations;

namespace Ledger.Infrastructure.Resilience;

public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    [Range(1, 60)] public int RequestTimeoutSeconds { get; init; } = 3;

    [Range(1, 300)] public int ShutdownTimeoutSeconds { get; init; } = 30;

    [Range(1, 60)] public int ServiceUnavailableRetryAfterSeconds { get; init; } = 1;

    public ResilienceHealthOptions Health { get; init; } = new();

    public ResilienceRetryOptions Retry { get; init; } = new();
}
