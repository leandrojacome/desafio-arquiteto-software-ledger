using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Ledger.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class OutboxSeeder(EmptyDatabase database)
{
    private const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    public async Task<IReadOnlyList<Guid>> InsertPendingAsync(
        int count,
        Guid? accountId = null,
        bool withTraceParent = false,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<Guid>(count);
        var account = accountId ?? Guid.NewGuid();

        await using var connection = await OpenAsApiAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        for (var version = 1; version <= count; version++)
        {
            var id = Guid.CreateVersion7();

            await InsertAsync(connection, transaction, id, account, version, withTraceParent, cancellationToken);

            ids.Add(id);
        }

        await transaction.CommitAsync(cancellationToken);

        return ids;
    }

    public async Task<IReadOnlyList<Guid>> InsertEventsAsync(
        IReadOnlyList<OutboxEventSeed> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        var ids = new List<Guid>(events.Count);

        await using var connection = await OpenAsApiAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var start = await DatabaseNowAsync(connection, transaction, cancellationToken) - TimeSpan.FromHours(1);

        foreach (var seed in events)
        {
            var id = Guid.CreateVersion7();

            await using var command = new NpgsqlCommand(
                """
                INSERT INTO outbox_messages (id, account_id, type, payload, correlation_id, created_at)
                VALUES (@id, @account_id, 'EntryRegistered', @payload, @correlation_id, @created_at)
                """,
                connection,
                transaction);

            var correlationId = Guid.NewGuid().ToString("N");

            command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id;
            command.Parameters.Add("account_id", NpgsqlDbType.Uuid).Value = seed.AccountId;
            command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = Payload(id, seed, correlationId);
            command.Parameters.Add("correlation_id", NpgsqlDbType.Text).Value = correlationId;
            command.Parameters.Add("created_at", NpgsqlDbType.TimestampTz).Value = start + seed.CreatedOffset;

            await command.ExecuteNonQueryAsync(cancellationToken);

            ids.Add(id);
        }

        await transaction.CommitAsync(cancellationToken);

        return ids;
    }

    public async Task MarkPendingAgainAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        await using var connection = await database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE outbox_messages SET published_at = NULL, locked_until = NULL WHERE id = ANY(@ids)",
            connection);

        command.Parameters.Add("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = ids.ToArray();

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> CountPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM outbox_messages WHERE published_at IS NULL",
            connection);

        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async Task<IReadOnlyList<OutboxRow>> ReadRowsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT id, attempts, published_at, locked_until, created_at, payload::text FROM outbox_messages ORDER BY created_at",
            connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<OutboxRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new OutboxRow(
                reader.GetGuid(0),
                reader.GetInt32(1),
                await ReadOptionalInstantAsync(reader, 2, cancellationToken),
                await ReadOptionalInstantAsync(reader, 3, cancellationToken),
                await reader.GetFieldValueAsync<DateTimeOffset>(4, cancellationToken),
                reader.GetString(5)));
        }

        return rows;
    }

    public async Task MarkPublishedAgoAsync(
        IReadOnlyCollection<Guid> ids,
        TimeSpan age,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        await using var connection = await database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE outbox_messages
            SET published_at = clock_timestamp() - make_interval(secs => @age_seconds),
                locked_until = NULL
            WHERE id = ANY(@ids)
            """,
            connection);

        command.Parameters.Add("age_seconds", NpgsqlDbType.Double).Value = age.TotalSeconds;
        command.Parameters.Add("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = ids.ToArray();

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> CountRowsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM outbox_messages", connection);

        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private static async Task<DateTimeOffset?> ReadOptionalInstantAsync(
        NpgsqlDataReader reader,
        int ordinal,
        CancellationToken cancellationToken)
    {
        return await reader.IsDBNullAsync(ordinal, cancellationToken)
            ? null
            : await reader.GetFieldValueAsync<DateTimeOffset>(ordinal, cancellationToken);
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        Guid accountId,
        int version,
        bool withTraceParent,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO outbox_messages (id, account_id, type, payload, correlation_id, traceparent)
            VALUES (@id, @account_id, 'EntryRegistered', @payload, @correlation_id, @traceparent)
            """,
            connection,
            transaction);

        var correlationId = Guid.NewGuid().ToString("N");

        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id;
        command.Parameters.Add("account_id", NpgsqlDbType.Uuid).Value = accountId;
        command.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = Payload(id, accountId, version, correlationId);
        command.Parameters.Add("correlation_id", NpgsqlDbType.Text).Value = correlationId;
        command.Parameters.Add("traceparent", NpgsqlDbType.Text).Value =
            withTraceParent ? TraceParent : DBNull.Value;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<DateTimeOffset> DatabaseNowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT clock_timestamp()", connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        await reader.ReadAsync(cancellationToken);

        return await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken);
    }

    private static string Payload(Guid id, OutboxEventSeed seed, string correlationId)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("eventId", id.ToString("D"));
            writer.WriteString("eventType", "EntryRegistered");
            writer.WriteNumber("schemaVersion", seed.SchemaVersion);
            writer.WriteString("accountId", seed.AccountId.ToString("D"));
            writer.WriteString("entryId", Guid.NewGuid().ToString("D"));
            writer.WriteNumber("accountVersion", seed.Version);
            writer.WriteString("type", "CREDIT");
            writer.WriteString("amount", "10.00");
            writer.WriteString("currency", "BRL");
            writer.WriteString("balanceAfter", seed.BalanceAfter.ToString("F2", CultureInfo.InvariantCulture));
            writer.WriteString("recordedAt", "2026-10-01T14:03:11.482913Z");
            writer.WriteString("occurredAt", "2026-10-01T14:03:10.000000Z");
            writer.WriteNull("reversesEntryId");
            writer.WriteString("correlationId", correlationId);

            if (seed.ExtraField is { } extra)
            {
                writer.WriteString(extra, "a field the consumer does not know");
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string Payload(Guid id, Guid accountId, int version, string correlationId)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("eventId", id.ToString("D"));
            writer.WriteString("eventType", "EntryRegistered");
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("accountId", accountId.ToString("D"));
            writer.WriteString("entryId", Guid.NewGuid().ToString("D"));
            writer.WriteNumber("accountVersion", version);
            writer.WriteString("type", "CREDIT");
            writer.WriteString("amount", "10.00");
            writer.WriteString("currency", "BRL");
            writer.WriteString("balanceAfter", (version * 10m).ToString("F2", CultureInfo.InvariantCulture));
            writer.WriteString("recordedAt", "2026-10-01T14:03:11.482913Z");
            writer.WriteString("occurredAt", "2026-10-01T14:03:10.000000Z");
            writer.WriteNull("reversesEntryId");
            writer.WriteString("correlationId", correlationId);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private async Task<NpgsqlConnection> OpenAsApiAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(PostgresConnectionString.Build(database.Settings, PostgresSource.Write));

        await connection.OpenAsync(cancellationToken);

        return connection;
    }
}

internal sealed record OutboxRow(
    Guid Id,
    int Attempts,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? LockedUntil,
    DateTimeOffset CreatedAt,
    string Payload);

internal sealed record OutboxEventSeed(
    Guid AccountId,
    long Version,
    decimal BalanceAfter,
    TimeSpan CreatedOffset,
    int SchemaVersion = 1,
    string? ExtraField = null);
