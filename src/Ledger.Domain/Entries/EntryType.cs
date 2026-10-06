using System.Diagnostics.CodeAnalysis;

namespace Ledger.Domain.Entries;

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let an unset entry type pass for a valid one; the default must stay outside the defined values.")]
public enum EntryType
{
    Credit = 1,
    Debit = 2
}
