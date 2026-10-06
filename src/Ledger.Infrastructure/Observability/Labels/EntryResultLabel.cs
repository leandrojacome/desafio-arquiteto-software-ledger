namespace Ledger.Infrastructure.Observability.Labels;

internal enum EntryResultLabel
{
    [Label("recorded")]
    Recorded = 0,

    [Label("replayed")]
    Replayed = 1,

    [Label("rejected")]
    Rejected = 2,

    [Label("failed")]
    Failed = 3
}
