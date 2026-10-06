using Ledger.Application.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Infrastructure.Persistence.Outbox;

internal sealed class PostgresIdempotencyKeyPruner(IPostgresConnectionFactory connectionFactory) : IIdempotencyKeyPruner
{
    private const string PruneIdempotencyKeysSql = """
        DELETE FROM idempotency_keys AS k
        USING (
            SELECT account_id, idempotency_key
            FROM idempotency_keys
            WHERE created_at < clock_timestamp() - make_interval(days => @retention_days)
            ORDER BY created_at
            LIMIT @batch_size
        ) AS expired
        WHERE k.account_id = expired.account_id
          AND k.idempotency_key = expired.idempotency_key;
        """;

    private const string PruneAccountCreationKeysSql = """
        DELETE FROM account_creation_keys AS k
        USING (
            SELECT client_id, idempotency_key
            FROM account_creation_keys
            WHERE created_at < clock_timestamp() - make_interval(days => @retention_days)
            ORDER BY created_at
            LIMIT @batch_size
        ) AS expired
        WHERE k.client_id = expired.client_id
          AND k.idempotency_key = expired.idempotency_key;
        """;

    public async Task<int> PruneAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(PostgresSource.Worker, cancellationToken);
        await using var entryKeys = new NpgsqlCommand(PruneIdempotencyKeysSql, connection);
        await using var accountKeys = new NpgsqlCommand(PruneAccountCreationKeysSql, connection);

        var removedEntryKeys = await ExecuteAsync(entryKeys, retention, batchSize, cancellationToken);
        var removedAccountKeys = await ExecuteAsync(accountKeys, retention, batchSize, cancellationToken);

        return removedEntryKeys + removedAccountKeys;
    }

    private static async Task<int> ExecuteAsync(
        NpgsqlCommand command,
        TimeSpan retention,
        int batchSize,
        CancellationToken cancellationToken)
    {
        command.Parameters.Add("retention_days", NpgsqlDbType.Integer).Value = (int)retention.TotalDays;
        command.Parameters.Add("batch_size", NpgsqlDbType.Integer).Value = batchSize;

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
