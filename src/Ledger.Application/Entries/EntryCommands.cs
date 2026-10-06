using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Entries;

public sealed record RegisterEntryCommand(
    AccountId AccountId,
    IdempotencyKey IdempotencyKey,
    EntryType Type,
    Money Amount,
    DateTimeOffset? OccurredAt,
    string? Description,
    string? Reference,
    string ClientId,
    string CorrelationId,
    string? TraceParent)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder
            .Append("AccountId = ").Append(AccountId)
            .Append(", Type = ").Append(Type)
            .Append(", ClientId = ").Append(ClientId)
            .Append(", CorrelationId = ").Append(CorrelationId);

        return true;
    }
}

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
