using System.Text;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Api.Validation;

internal sealed record RegisterEntryInput(
    EntryType Type,
    Money Amount,
    DateTimeOffset? OccurredAt,
    string? Description,
    string? Reference)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Type = ").Append(Type);

        return true;
    }
}
