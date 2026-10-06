namespace Ledger.Infrastructure.Observability.Labels;

internal enum BalanceMode
{
    [Label("current")]
    Current = 0,

    [Label("as_of")]
    AsOf = 1
}
