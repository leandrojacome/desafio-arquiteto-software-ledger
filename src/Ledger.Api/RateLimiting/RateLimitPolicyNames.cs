namespace Ledger.Api.RateLimiting;

internal static class RateLimitPolicyNames
{
    public const string WritePerClient = "write-per-client";

    public const string ReadPerClient = "read-per-client";

    public const string WritePerAccount = "write-per-account";

    public const string WriteConcurrency = "write-concurrency";

    public const string BalanceConcurrency = "balance-concurrency";

    public const string StatementConcurrency = "statement-concurrency";

    public static bool IsConcurrency(string policy) =>
        policy is WriteConcurrency or BalanceConcurrency or StatementConcurrency;
}
