namespace Ledger.Infrastructure.Observability.Labels;

internal enum RewrapResult
{
    [Label("rewrapped")]
    Rewrapped = 0,

    [Label("failed")]
    Failed = 1
}
