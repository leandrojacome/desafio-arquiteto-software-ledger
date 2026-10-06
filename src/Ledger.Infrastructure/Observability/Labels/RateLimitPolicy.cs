namespace Ledger.Infrastructure.Observability.Labels;

internal enum RateLimitPolicy
{
    [Label("write-per-client")]
    WritePerClient = 0,

    [Label("read-per-client")]
    ReadPerClient = 1,

    [Label("write-per-account")]
    WritePerAccount = 2,

    [Label("write-concurrency")]
    WriteConcurrency = 3,

    [Label("balance-concurrency")]
    BalanceConcurrency = 4,

    [Label("statement-concurrency")]
    StatementConcurrency = 5
}
