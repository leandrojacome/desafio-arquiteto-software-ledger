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
