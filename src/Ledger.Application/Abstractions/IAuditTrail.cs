using Ledger.Application.Audit;

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
