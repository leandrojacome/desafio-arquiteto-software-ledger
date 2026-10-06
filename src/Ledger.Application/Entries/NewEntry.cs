using System.Text;
using Ledger.Domain.Entries;

namespace Ledger.Application.Entries;

public sealed record NewEntry(Entry Entry, string ClientId, string CorrelationId)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("EntryId = ").Append(Entry.Id).Append(", AccountId = ").Append(Entry.AccountId);

        return true;
    }
}
