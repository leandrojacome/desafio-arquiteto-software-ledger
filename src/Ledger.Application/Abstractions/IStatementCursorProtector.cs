using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Abstractions;

public interface IStatementCursorProtector
{
    string Protect(AccountId accountId, StatementPosition position);

    Result<StatementPosition> Unprotect(AccountId accountId, string cursor);
}
