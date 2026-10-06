using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Infrastructure.Persistence.Outbox;

internal sealed class PostgresOutboxQueue(IPostgresConnectionFactory connectionFactory) : IOutboxQueue
{
    private const string ClaimOutboxBatchSql = """
        UPDATE outbox_messages
        SET locked_until = clock_timestamp() + make_interval(secs => @lease_seconds),
            attempts = attempts + 1
        WHERE id IN (
            SELECT id FROM outbox_messages
            WHERE published_at IS NULL
              AND (locked_until IS NULL OR locked_until < clock_timestamp())
            ORDER BY created_at
            LIMIT @batch_size
            FOR UPDATE SKIP LOCKED
        )
        RETURNING id, account_id, type, payload::text AS payload, correlation_id, traceparent, created_at, attempts;
        """;

    private const string MarkOutboxPublishedSql = """
        UPDATE outbox_messages
        SET published_at = clock_timestamp(),
            locked_until = NULL
        WHERE id = ANY(@ids);
        """;

    private const string ReleaseOutboxSql = """
        UPDATE outbox_messages
        SET locked_until = NULL,
            attempts = GREATEST(attempts - 1, 0)
        WHERE id = ANY(@ids)
          AND published_at IS NULL;
        """;

    private const string PruneOutboxSql = """
        DELETE FROM outbox_messages
        WHERE id IN (
            SELECT id FROM outbox_messages
            WHERE published_at < clock_timestamp() - make_interval(days => @retention_days)
            ORDER BY published_at
            LIMIT @batch_size
            FOR UPDATE SKIP LOCKED
        );
        """;

    private const string ReadOutboxStatsSql = """
        SELECT (SELECT count(*)
                FROM (SELECT 1 FROM outbox_messages WHERE published_at IS NULL LIMIT @pending_cap) AS pending) AS pending,
               (SELECT EXTRACT(EPOCH FROM clock_timestamp() - min(created_at))::double precision
                FROM outbox_messages WHERE published_at IS NULL) AS oldest_age_seconds,
               (SELECT count(*)
                FROM (SELECT attempts FROM outbox_messages WHERE published_at IS NULL ORDER BY created_at LIMIT @head_window) AS head
                WHERE head.attempts >= @failed_attempts) AS failed;
        """;

    public async Task<IReadOnlyList<OutboxEnvelope>> ClaimBatchAsync(
        int batchSize,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(PostgresSource.Worker, cancellationToken);
        await using var command = new NpgsqlCommand(ClaimOutboxBatchSql, connection);

        command.Parameters.Add("lease_seconds", NpgsqlDbType.Double).Value = lease.TotalSeconds;
        command.Parameters.Add("batch_size", NpgsqlDbType.Integer).Value = batchSize;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var envelopes = new List<OutboxEnvelope>(batchSize);

        while (await reader.ReadAsync(cancellationToken))
        {
            envelopes.Add(Map(reader));
        }

        return envelopes;
    }

    public async Task<int> MarkPublishedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return 0;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(PostgresSource.Worker, cancellationToken);
        await using var command = new NpgsqlCommand(MarkOutboxPublishedSql, connection);

        command.Parameters.Add("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = ids.ToArray();

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> ReleaseAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return 0;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(PostgresSource.Worker, cancellationToken);
        await using var command = new NpgsqlCommand(ReleaseOutboxSql, connection);

        command.Parameters.Add("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = ids.ToArray();

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> PruneAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(PostgresSource.Worker, cancellationToken);
        await using var command = new NpgsqlCommand(PruneOutboxSql, connection);

        command.Parameters.Add("retention_days", NpgsqlDbType.Integer).Value = WholeDays(retention);
        command.Parameters.Add("batch_size", NpgsqlDbType.Integer).Value = batchSize;

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OutboxStats> ReadStatsAsync(OutboxStatsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await connectionFactory.OpenConnectionAsync(PostgresSource.Worker, cancellationToken);
        await using var command = new NpgsqlCommand(ReadOutboxStatsSql, connection);

        command.Parameters.Add("pending_cap", NpgsqlDbType.Integer).Value = request.PendingCap;
        command.Parameters.Add("head_window", NpgsqlDbType.Integer).Value = request.FailedHeadWindow;
        command.Parameters.Add("failed_attempts", NpgsqlDbType.Integer).Value = request.FailedAttempts;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        await reader.ReadAsync(cancellationToken);

        double? oldestAge = await reader.IsDBNullAsync(1, cancellationToken)
            ? null
            : reader.GetDouble(1);

        return new OutboxStats(reader.GetInt64(0), oldestAge, reader.GetInt64(2));
    }

    private static int WholeDays(TimeSpan retention) => Math.Max(1, (int)Math.Ceiling(retention.TotalDays));

    private static OutboxEnvelope Map(NpgsqlDataReader reader)
    {
        return new OutboxEnvelope(
            reader.GetGuid(0),
            AccountId.From(reader.GetGuid(1)).Value,
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetInt32(7));
    }
}
