using System.Text;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Audit;

public sealed record AuditEvent
{
    internal AuditEvent(
        string eventType,
        string clientId,
        AccountId? accountId,
        string correlationId,
        string outcome,
        string detailsJson)
    {
        EventType = eventType;
        ClientId = clientId;
        AccountId = accountId;
        CorrelationId = correlationId;
        Outcome = outcome;
        DetailsJson = detailsJson;
    }

    public string EventType { get; }

    public string ClientId { get; }

    public AccountId? AccountId { get; }

    public string CorrelationId { get; }

    public string Outcome { get; }

    public string DetailsJson { get; }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("EventType = ").Append(EventType).Append(", Outcome = ").Append(Outcome);

        return true;
    }
}
