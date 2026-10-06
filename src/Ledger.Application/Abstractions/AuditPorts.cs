using Ledger.Application.Audit;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Abstractions;

public interface IAuditTrail
{
    Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken);

    Task<bool> ContainsAsync(
        string eventType,
        string detailName,
        string detailValue,
        CancellationToken cancellationToken);
}

public interface IDeniedWriteAuditor
{
    void Record(
        string clientId,
        AccountId? accountId,
        string correlationId,
        string route,
        DeniedWriteReason reason);
}
