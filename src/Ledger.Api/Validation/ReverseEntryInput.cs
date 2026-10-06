using System.Text;

namespace Ledger.Api.Validation;

internal sealed record ReverseEntryInput(string? Description)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("HasDescription = ").Append(Description is not null);

        return true;
    }
}
