using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Entries;

public sealed record EntryView(
    EntryId Id,
    AccountId AccountId,
    long AccountVersion,
    EntryType Type,
    Money Amount,
    Money BalanceAfter,
    DateTimeOffset RecordedAt,
    DateTimeOffset OccurredAt,
    string? Description,
    string? Reference,
    EntryId? ReversesEntryId)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id).Append(", AccountId = ").Append(AccountId);

        return true;
    }
}
