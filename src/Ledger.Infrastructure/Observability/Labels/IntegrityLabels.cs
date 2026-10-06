using Ledger.Application.Integrity;

namespace Ledger.Infrastructure.Observability.Labels;

internal enum IntegrityResult
{
    [Label("ok")]
    Ok = 0,

    [Label("violation")]
    Violation = 1,

    [Label("error")]
    Error = 2
}

internal enum IntegrityViolationKind
{
    [Label(IntegrityCheckExtensions.BalanceMismatchKind)]
    BalanceMismatch = 0,

    [Label(IntegrityCheckExtensions.ChainBrokenKind)]
    ChainBroken = 1
}
