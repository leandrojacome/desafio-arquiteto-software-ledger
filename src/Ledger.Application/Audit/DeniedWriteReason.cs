using System.Diagnostics.CodeAnalysis;

namespace Ledger.Application.Audit;

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let an unset reason pass for a valid one; the default must stay outside the defined values.")]
public enum DeniedWriteReason
{
    InsufficientScope = 1,
    NotProvisioningClient = 2
}
