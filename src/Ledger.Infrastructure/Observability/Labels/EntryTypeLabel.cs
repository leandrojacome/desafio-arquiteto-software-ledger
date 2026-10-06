namespace Ledger.Infrastructure.Observability.Labels;

internal enum EntryTypeLabel
{
    [Label("credit")]
    Credit = 0,

    [Label("debit")]
    Debit = 1,

    [Label("reversal")]
    Reversal = 2
}
