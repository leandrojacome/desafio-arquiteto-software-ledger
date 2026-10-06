using System.ComponentModel.DataAnnotations;

namespace Ledger.Api.RateLimiting;

internal sealed class TokenBucketSettings
{
    [Range(1, 1_000_000)] public int Capacity { get; init; } = 100;

    [Range(1, 1_000_000)] public int RefillPerSecond { get; init; } = 50;
}
