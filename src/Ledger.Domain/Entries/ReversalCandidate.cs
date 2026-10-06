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
