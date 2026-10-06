using System.ComponentModel.DataAnnotations;

namespace Ledger.Infrastructure.Resilience;

public sealed class ResilienceHealthOptions
{
    [Range(1, 30)] public int ProbeTimeoutSeconds { get; init; } = 1;

    [Range(1, 60)] public int CacheSeconds { get; init; } = 5;

    [Range(1, 60)] public int RetryAfterSeconds { get; init; } = 5;
}
