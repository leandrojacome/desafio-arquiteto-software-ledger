using System.Text;
using Ledger.Application.Entries;

namespace Ledger.Application.Abstractions;

public sealed record IdempotencyRecord(ReadOnlyMemory<byte> RequestHash, int HashVersion, EntryView Entry)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("EntryId = ").Append(Entry.Id).Append(", HashVersion = ").Append(HashVersion);

        return true;
    }
}
