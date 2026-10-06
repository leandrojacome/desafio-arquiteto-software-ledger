using System.Text;

namespace Ledger.Application.Entries;

public sealed record StatementPage(IReadOnlyList<EntryView> Items, string? NextCursor, int Limit)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Returned = ").Append(Items.Count).Append(", Limit = ").Append(Limit);

        return true;
    }
}
