using System.Diagnostics.CodeAnalysis;

namespace Ledger.Application.Integrity;

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let an unset mode pass for a valid one; the default must stay outside the defined values.")]
public enum IntegrityMode
{
    Recent = 1,
    Full = 2
}
