using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Entries;

public sealed record ListEntriesQuery(
    AccountId AccountId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Limit,
    StatementPosition? Cursor,
    string ClientId,
    string CorrelationId);

public sealed record StatementPage(IReadOnlyList<EntryView> Items, string? NextCursor, int Limit)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Returned = ").Append(Items.Count).Append(", Limit = ").Append(Limit);

        return true;
    }
}
