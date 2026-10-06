using Ledger.Application.Audit;

namespace Ledger.Infrastructure.Observability.Labels;

internal enum AuditOutcomeLabel
{
    [Label(AuditOutcome.Success)]
    Success = 0,

    [Label(AuditOutcome.Denied)]
    Denied = 1
}
