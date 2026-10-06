using Ledger.Application.Integrity;

namespace Ledger.Infrastructure.Observability.Labels;

internal enum IntegrityViolationKind
{
    [Label(IntegrityCheckExtensions.BalanceMismatchKind)]
    BalanceMismatch = 0,

    [Label(IntegrityCheckExtensions.ChainBrokenKind)]
    ChainBroken = 1
}
