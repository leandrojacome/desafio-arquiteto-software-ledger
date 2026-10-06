namespace Ledger.Infrastructure.Observability.Labels;

internal enum AuthFailureReason
{
    [Label("missing_token")]
    MissingToken = 0,

    [Label("invalid_token")]
    InvalidToken = 1,

    [Label("expired")]
    Expired = 2,

    [Label("insufficient_scope")]
    InsufficientScope = 3,

    [Label("missing_client_id")]
    MissingClientId = 4,

    [Label("invalid_client_id")]
    InvalidClientId = 5
}

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
