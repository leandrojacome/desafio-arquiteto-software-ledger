using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Balances;

public sealed record BalanceView(
    AccountId AccountId,
    Money Balance,
    Money OverdraftLimit,
    EntryId? LastEntryId,
    DateTimeOffset AsOf,
    bool? Settled)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("AccountId = ").Append(AccountId);

        return true;
    }
}
