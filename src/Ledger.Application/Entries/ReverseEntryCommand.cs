using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Entries;

public sealed record ReverseEntryCommand(
    AccountId AccountId,
    EntryId OriginalEntryId,
    IdempotencyKey IdempotencyKey,
    string? Description,
    string ClientId,
    string CorrelationId,
    string? TraceParent)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder
            .Append("AccountId = ").Append(AccountId)
            .Append(", OriginalEntryId = ").Append(OriginalEntryId)
            .Append(", ClientId = ").Append(ClientId)
            .Append(", CorrelationId = ").Append(CorrelationId);

        return true;
    }
}
