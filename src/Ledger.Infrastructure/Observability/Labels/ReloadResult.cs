namespace Ledger.Infrastructure.Observability.Labels;

internal enum ReloadResult
{
    [Label("ok")]
    Ok = 0,

    [Label("failed")]
    Failed = 1
}
