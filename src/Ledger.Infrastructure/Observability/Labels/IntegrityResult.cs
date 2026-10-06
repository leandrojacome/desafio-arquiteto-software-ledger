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
