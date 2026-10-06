using Dapper;
using Ledger.Application.Abstractions;
using Npgsql;

namespace Ledger.Infrastructure.Persistence.Outbox;

internal sealed class PostgresOutbox(NpgsqlConnection connection, NpgsqlTransaction transaction) : IOutbox
{
    private const string EnqueueEventSql = """
                                           INSERT INTO outbox_messages (id, account_id, type, payload, correlation_id, traceparent)
                                           VALUES (@event_id, @account_id, @type, @payload, @correlation_id, @traceparent);
                                           """;

    public async Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var parameters = new SqlParameters()
            .Uuid("event_id", message.Id)
            .Uuid("account_id", message.AccountId.Value)
            .Varchar("type", message.Type)
            .Jsonb("payload", message.Payload)
            .Varchar("correlation_id", message.CorrelationId)
            .Varchar("traceparent", message.TraceParent)
            .Build();

        var command = new CommandDefinition(
            EnqueueEventSql,
            parameters,
            transaction,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }
}
