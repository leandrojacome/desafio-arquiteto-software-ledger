using System.ComponentModel.DataAnnotations;

namespace Ledger.Infrastructure.Resilience;

public sealed class ResilienceRetryOptions
{
    [Range(0, 5)] public int MaxRetryAttempts { get; init; } = 2;

    [Range(10, 1000)] public int BaseDelayMs { get; init; } = 50;
}
