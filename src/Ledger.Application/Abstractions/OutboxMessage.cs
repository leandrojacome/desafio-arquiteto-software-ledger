using System.Text;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Abstractions;

public sealed record OutboxMessage(
    Guid Id,
    AccountId AccountId,
    string Type,
    string Payload,
    string CorrelationId,
    string? TraceParent)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id).Append(", AccountId = ").Append(AccountId).Append(", Type = ").Append(Type);

        return true;
    }
}
