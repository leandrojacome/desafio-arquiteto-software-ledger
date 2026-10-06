using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Integrity;

public sealed record ChainRow(
    AccountId AccountId,
    long AccountVersion,
    EntryId EntryId,
    decimal BalanceAfter,
    DateTimeOffset RecordedAt,
    DateTimeOffset? PreviousRecordedAt,
    decimal BalanceDrift,
    bool MissingPredecessor,
    bool NonMonotonic)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder
            .Append("AccountId = ").Append(AccountId)
            .Append(", EntryId = ").Append(EntryId)
            .Append(", AccountVersion = ").Append(AccountVersion);

        return true;
    }
}

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
