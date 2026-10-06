using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Balances;

public sealed record GetBalanceQuery(AccountId AccountId, DateTimeOffset? AsOf, string ClientId, string CorrelationId);

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

public sealed record BalanceReadSettings(TimeSpan SettlingWindow);
