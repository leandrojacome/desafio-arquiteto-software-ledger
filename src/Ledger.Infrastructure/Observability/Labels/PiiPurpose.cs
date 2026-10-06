namespace Ledger.Infrastructure.Observability.Labels;

internal enum PiiPurpose
{
    [Label("rewrap")]
    Rewrap = 0,

    [Label("holder_lookup")]
    HolderLookup = 1,

    [Label("investigation")]
    Investigation = 2
}
