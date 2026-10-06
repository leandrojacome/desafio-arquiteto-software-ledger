using Ledger.Application.Audit;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Abstractions;

public interface IDeniedWriteAuditor
{
    void Record(
        string clientId,
        AccountId? accountId,
        string correlationId,
        string route,
        DeniedWriteReason reason);
}
