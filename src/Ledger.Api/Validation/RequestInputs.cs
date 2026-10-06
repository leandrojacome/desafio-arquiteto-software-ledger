using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Api.Validation;

internal sealed record CreateAccountInput(HolderDocument HolderDocument, string Currency, Money OverdraftLimit)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Currency = ").Append(Currency);

        return true;
    }
}

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

internal sealed record ReverseEntryInput(string? Description)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("HasDescription = ").Append(Description is not null);

        return true;
    }
}

internal sealed record StatementInput(DateTimeOffset? From, DateTimeOffset? To, int Limit, StatementPosition? Cursor);
