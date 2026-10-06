namespace Ledger.Infrastructure.Observability.Labels;

internal enum CreateAccountOutcome
{
    [Label("created")]
    Created = 0,

    [Label("already_created")]
    AlreadyCreated = 1,

    [Label("key_unavailable")]
    KeyUnavailable = 2,

    [Label("failed")]
    Failed = 3
}

internal enum BalanceMode
{
    [Label("current")]
    Current = 0,

    [Label("as_of")]
    AsOf = 1
}
