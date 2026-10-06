using System.ComponentModel.DataAnnotations;

namespace Ledger.Api.RateLimiting;

internal sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; init; } = true;

    [Required] public TokenBucketSettings WritePerClient { get; init; } = new();

    [Required] public TokenBucketSettings ReadPerClient { get; init; } = new();

    [Required] public TokenBucketSettings WritePerAccount { get; init; } = new();

    [Range(1, 3600)] public int ReplenishmentSeconds { get; init; } = 1;

    [Range(1, 256)] public int WriteConcurrency { get; init; } = 16;

    [Range(1, 256)] public int BalanceConcurrency { get; init; } = 16;

    [Range(1, 256)] public int StatementConcurrency { get; init; } = 8;
}
