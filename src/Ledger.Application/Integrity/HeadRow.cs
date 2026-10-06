using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Integrity;

public sealed record HeadRow(
    AccountId AccountId,
    decimal Balance,
    decimal OverdraftLimit,
    long Version,
    EntryId? LastEntryId,
    decimal? LatestBalanceAfter,
    long? LatestAccountVersion,
    EntryId? LatestEntryId)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("AccountId = ").Append(AccountId);

        return true;
    }
}
