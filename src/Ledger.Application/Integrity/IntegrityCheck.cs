using System.Diagnostics.CodeAnalysis;

namespace Ledger.Application.Integrity;

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let an unset check pass for a valid one; the default must stay outside the defined values.")]
public enum IntegrityCheck
{
    HeadBalance = 1,
    HeadVersion = 2,
    HeadLastEntry = 3,
    HeadFloor = 4,
    ChainDrift = 5,
    ChainGap = 6,
    ChainNonMonotonic = 7,
    SumBalance = 8
}
