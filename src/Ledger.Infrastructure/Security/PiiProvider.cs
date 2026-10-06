using System.Diagnostics.CodeAnalysis;

namespace Ledger.Infrastructure.Security;

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let an unset provider pass for a valid one; the default must stay outside the defined values.")]
internal enum PiiProvider
{
    Directory = 1,
    Configuration = 2
}
