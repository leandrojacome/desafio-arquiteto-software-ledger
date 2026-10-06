using System.Text;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Outbox;

public sealed record OutboxEnvelope(
    Guid Id,
    AccountId AccountId,
    string Type,
    string Payload,
    string CorrelationId,
    string? TraceParent,
    DateTimeOffset CreatedAt,
    int Attempts)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder
            .Append("Id = ").Append(Id)
            .Append(", AccountId = ").Append(AccountId)
            .Append(", Type = ").Append(Type)
            .Append(", Attempts = ").Append(Attempts);

        return true;
    }
}
