using System.Diagnostics.CodeAnalysis;

namespace Ledger.Api.RateLimiting;

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let a route without a declared class pass for a valid one; the default must stay outside the defined values.")]
internal enum RequestClass
{
    Write = 1,
    Balance = 2,
    Statement = 3
}
