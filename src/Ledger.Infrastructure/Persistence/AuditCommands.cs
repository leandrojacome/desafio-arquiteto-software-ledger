using Dapper;
using Ledger.Application.Audit;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal static class AuditCommands
{
    private const string InsertAuditSql = """
                                          INSERT INTO audit_log (event_type, client_id, account_id, correlation_id, outcome, details)
                                          VALUES (@event_type, @client_id, @account_id, @correlation_id, @outcome, @details);
                                          """;

    private const string AuditContainsSql = """
                                            SELECT EXISTS (
                                                SELECT 1
                                                FROM audit_log
                                                WHERE event_type = @event_type
                                                  AND details ->> @detail_name = @detail_value
                                            );
                                            """;

    public static CommandDefinition Insert(
        AuditEvent auditEvent,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters()
            .Varchar("event_type", auditEvent.EventType)
            .Varchar("client_id", auditEvent.ClientId)
            .Uuid("account_id", auditEvent.AccountId?.Value)
            .Varchar("correlation_id", auditEvent.CorrelationId)
            .Varchar("outcome", auditEvent.Outcome)
            .Jsonb("details", auditEvent.DetailsJson)
            .Build();

        return new CommandDefinition(InsertAuditSql, parameters, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Contains(
        string eventType,
        string detailName,
        string detailValue,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters()
            .Varchar("event_type", eventType)
            .Varchar("detail_name", detailName)
            .Varchar("detail_value", detailValue)
            .Build();

        return new CommandDefinition(AuditContainsSql, parameters, transaction, cancellationToken: cancellationToken);
    }
}
