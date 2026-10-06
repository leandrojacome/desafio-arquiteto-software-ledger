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

internal enum RewrapResult
{
    [Label("rewrapped")]
    Rewrapped = 0,

    [Label("failed")]
    Failed = 1
}

internal enum ReloadResult
{
    [Label("ok")]
    Ok = 0,

    [Label("failed")]
    Failed = 1
}
