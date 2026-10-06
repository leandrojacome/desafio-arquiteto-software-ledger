using System.Text;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Entries;

public sealed record ReversalPlan
{
    internal ReversalPlan(EntryId originalId, EntryType type, Money amount)
    {
        OriginalId = originalId;
        Type = type;
        Amount = amount;
    }

    public EntryId OriginalId { get; }

    public EntryType Type { get; }

    public Money Amount { get; }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("OriginalId = ").Append(OriginalId).Append(", Type = ").Append(Type);

        return true;
    }
}
