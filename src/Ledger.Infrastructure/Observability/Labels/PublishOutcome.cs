namespace Ledger.Infrastructure.Observability.Labels;

internal enum PublishOutcome
{
    [Label("confirmed")]
    Confirmed = 0,

    [Label("failed")]
    Failed = 1
}
