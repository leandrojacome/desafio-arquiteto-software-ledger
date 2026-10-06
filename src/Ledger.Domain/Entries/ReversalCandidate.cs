using System.Text;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Entries;

public sealed record ReversalCandidate(
    EntryId Id,
    EntryType Type,
    Money Amount,
    EntryId? ReversesEntryId,
    EntryId? ReversalId)
{
    public Result<ReversalPlan> Plan()
    {
        if (ReversesEntryId is not null)
        {
            return EntryErrors.NotReversible;
        }

        if (ReversalId is not null)
        {
            return EntryErrors.AlreadyReversed;
        }

        return new ReversalPlan(Id, Type.Opposite(), Amount);
    }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id).Append(", Type = ").Append(Type);

        return true;
    }
}

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
