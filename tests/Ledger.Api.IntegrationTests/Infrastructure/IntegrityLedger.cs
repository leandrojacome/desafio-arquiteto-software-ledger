using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Api.IntegrationTests.Infrastructure;

[SuppressMessage("Security", "CA2100",
    Justification = "Every caller passes literal SQL and the values travel as parameters.")]
internal sealed class IntegrityLedger(EmptyDatabase database)
{
    private static readonly (string Type, decimal Amount)[] DefaultMovements =
    [
        ("CREDIT", 100.00m),
        ("DEBIT", 30.00m),
        ("CREDIT", 50.00m),
        ("DEBIT", 20.00m),
        ("CREDIT", 10.00m)
    ];

    private static readonly (string Type, decimal Amount)[] OverdrawnMovements =
    [
        ("CREDIT", 100.00m),
        ("DEBIT", 130.00m)
    ];

    public Task<SeededAccount> SeedAsync(
        DateTimeOffset? firstRecordedAt = null,
        CancellationToken cancellationToken = default)
    {
        return SeedAsync(DefaultMovements, 0m, firstRecordedAt, cancellationToken);
    }

    public Task<SeededAccount> SeedOverdrawnAsync(
        DateTimeOffset? firstRecordedAt = null,
        CancellationToken cancellationToken = default)
    {
        return SeedAsync(OverdrawnMovements, 0m, firstRecordedAt, cancellationToken);
    }

    public async Task<SeededAccount> SeedAsync(
        IReadOnlyList<(string Type, decimal Amount)> movements,
        decimal overdraftLimit,
        DateTimeOffset? firstRecordedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movements);

        var accountId = Guid.NewGuid();
        var start = firstRecordedAt ?? await DatabaseNowAsync(cancellationToken) - TimeSpan.FromMinutes(4);
        var entries = new List<SeededEntry>(movements.Count);
        var balance = 0m;

        for (var index = 0; index < movements.Count; index++)
        {
            var (type, amount) = movements[index];

            balance += type == "CREDIT" ? amount : -amount;

            entries.Add(new SeededEntry(
                Guid.CreateVersion7(),
                index + 1L,
                type,
                amount,
                balance,
                start + TimeSpan.FromSeconds(10 * (index + 1))));
        }

        await using var connection = await database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var belowFloor = balance < -overdraftLimit;

        if (belowFloor)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "ALTER TABLE account_balances DROP CONSTRAINT ck_account_balances_balance_floor",
                cancellationToken);
        }

        await InsertAccountAsync(connection, transaction, accountId, cancellationToken);
        await InsertBalanceAsync(connection, transaction, accountId, balance, overdraftLimit, entries, cancellationToken);

        foreach (var entry in entries)
        {
            await InsertEntryAsync(connection, transaction, accountId, entry, cancellationToken);
        }

        if (belowFloor)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "ALTER TABLE account_balances ADD CONSTRAINT ck_account_balances_balance_floor CHECK (balance >= -overdraft_limit) NOT VALID",
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new SeededAccount(accountId, entries);
    }

    public Task TamperStoredBalanceAsync(Guid accountId, decimal delta, CancellationToken cancellationToken = default)
    {
        return RunAsync(
            "UPDATE account_balances SET balance = balance + @delta WHERE account_id = @id",
            [("delta", NpgsqlDbType.Numeric, delta), ("id", NpgsqlDbType.Uuid, accountId)],
            cancellationToken);
    }

    public Task TamperVersionAsync(Guid accountId, long delta, CancellationToken cancellationToken = default)
    {
        return RunAsync(
            "UPDATE account_balances SET version = version + @delta WHERE account_id = @id",
            [("delta", NpgsqlDbType.Bigint, delta), ("id", NpgsqlDbType.Uuid, accountId)],
            cancellationToken);
    }

    public Task TamperLastEntryAsync(Guid accountId, Guid entryId, CancellationToken cancellationToken = default)
    {
        return RunAsync(
            "UPDATE account_balances SET last_entry_id = @entry WHERE account_id = @id",
            [("entry", NpgsqlDbType.Uuid, entryId), ("id", NpgsqlDbType.Uuid, accountId)],
            cancellationToken);
    }

    public Task TamperBalanceAfterAsync(Guid entryId, decimal delta, CancellationToken cancellationToken = default)
    {
        return RunWithImmutabilityOffAsync(
            "UPDATE ledger_entries SET balance_after = balance_after + @delta WHERE id = @id",
            [("delta", NpgsqlDbType.Numeric, delta), ("id", NpgsqlDbType.Uuid, entryId)],
            cancellationToken);
    }

    public Task TamperRecordedAtAsync(Guid entryId, DateTimeOffset recordedAt, CancellationToken cancellationToken = default)
    {
        return RunWithImmutabilityOffAsync(
            "UPDATE ledger_entries SET recorded_at = @recorded_at WHERE id = @id",
            [("recorded_at", NpgsqlDbType.TimestampTz, recordedAt), ("id", NpgsqlDbType.Uuid, entryId)],
            cancellationToken);
    }

    public Task DeleteEntryAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        return RunWithImmutabilityOffAsync(
            "DELETE FROM ledger_entries WHERE id = @id",
            [("id", NpgsqlDbType.Uuid, entryId)],
            cancellationToken);
    }

    public async Task InsertEntryDirectlyAsync(
        Guid accountId,
        SeededEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = await database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await InsertEntryAsync(connection, transaction, accountId, entry, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task InsertRunCompletedAsync(
        Ledger.Application.Integrity.IntegrityMode mode,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken = default)
    {
        var details = Ledger.Infrastructure.Persistence.Integrity.IntegrityAuditDetails.ForRun(
            new Ledger.Application.Integrity.IntegrityRunSummary(mode, false, windowStart, windowEnd, 0, 0, 0));

        await RunAsync(
            """
            INSERT INTO audit_log (event_type, client_id, account_id, correlation_id, outcome, details)
            VALUES ('integrity.run_completed', 'ledger-worker', NULL, 'seeded-run', 'SUCCESS', @details::jsonb)
            """,
            [("details", NpgsqlDbType.Text, details)],
            cancellationToken);
    }

    public async Task<string> FingerprintAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT (SELECT md5(coalesce(string_agg(e::text, '|' ORDER BY e.id), '')) FROM ledger_entries AS e)
                || (SELECT md5(coalesce(string_agg(b::text, '|' ORDER BY b.account_id), '')) FROM account_balances AS b)
                || (SELECT md5(coalesce(string_agg(o::text, '|' ORDER BY o.id), '')) FROM outbox_messages AS o)
            """,
            connection);

        return (string)(await command.ExecuteScalarAsync(cancellationToken) ?? string.Empty);
    }

    public async Task<DateTimeOffset> DatabaseNowAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT clock_timestamp()", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        await reader.ReadAsync(cancellationToken);

        return await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken);
    }

    public async Task<IReadOnlyList<AuditRow>> AuditRowsAsync(
        string? eventTypePrefix = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT event_type, client_id, account_id, correlation_id, outcome, details::text
            FROM audit_log
            WHERE event_type LIKE @prefix
            ORDER BY id
            """,
            connection);

        command.Parameters.Add("prefix", NpgsqlDbType.Text).Value = (eventTypePrefix ?? string.Empty) + "%";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<AuditRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AuditRow(
                reader.GetString(0),
                reader.GetString(1),
                await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return rows;
    }

    private async Task RunWithImmutabilityOffAsync(
        string sql,
        IReadOnlyList<(string Name, NpgsqlDbType Type, object Value)> parameters,
        CancellationToken cancellationToken)
    {
        await RunAsync(
            "ALTER TABLE ledger_entries DISABLE TRIGGER tr_ledger_entries_forbid_update_delete",
            [],
            cancellationToken);

        try
        {
            await RunAsync(sql, parameters, cancellationToken);
        }
        finally
        {
            await RunAsync(
                "ALTER TABLE ledger_entries ENABLE TRIGGER tr_ledger_entries_forbid_update_delete",
                [],
                CancellationToken.None);
        }
    }

    private async Task RunAsync(
        string sql,
        IReadOnlyList<(string Name, NpgsqlDbType Type, object Value)> parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, type, value) in parameters)
        {
            command.Parameters.Add(name, type).Value = value;
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO accounts (id, currency) VALUES (@id, 'BRL')",
            connection,
            transaction);

        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = accountId;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertBalanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid accountId,
        decimal balance,
        decimal overdraftLimit,
        IReadOnlyList<SeededEntry> entries,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO account_balances (account_id, balance, overdraft_limit, version, last_entry_id, last_recorded_at)
            VALUES (@id, @balance, @overdraft_limit, @version, @last_entry_id, @last_recorded_at)
            """,
            connection,
            transaction);

        var last = entries[^1];

        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = accountId;
        command.Parameters.Add("balance", NpgsqlDbType.Numeric).Value = balance;
        command.Parameters.Add("overdraft_limit", NpgsqlDbType.Numeric).Value = overdraftLimit;
        command.Parameters.Add("version", NpgsqlDbType.Bigint).Value = last.Version;
        command.Parameters.Add("last_entry_id", NpgsqlDbType.Uuid).Value = last.Id;
        command.Parameters.Add("last_recorded_at", NpgsqlDbType.TimestampTz).Value = last.RecordedAt;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEntryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid accountId,
        SeededEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO ledger_entries (id, account_id, account_version, type, amount, currency, balance_after,
                                        recorded_at, occurred_at, client_id, correlation_id)
            VALUES (@id, @account_id, @version, @type, @amount, 'BRL', @balance_after,
                    @recorded_at, @recorded_at, 'integrity-tests', 'integrity-tests')
            """,
            connection,
            transaction);

        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = entry.Id;
        command.Parameters.Add("account_id", NpgsqlDbType.Uuid).Value = accountId;
        command.Parameters.Add("version", NpgsqlDbType.Bigint).Value = entry.Version;
        command.Parameters.Add("type", NpgsqlDbType.Text).Value = entry.Type;
        command.Parameters.Add("amount", NpgsqlDbType.Numeric).Value = entry.Amount;
        command.Parameters.Add("balance_after", NpgsqlDbType.Numeric).Value = entry.BalanceAfter;
        command.Parameters.Add("recorded_at", NpgsqlDbType.TimestampTz).Value = entry.RecordedAt;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

internal sealed record SeededEntry(
    Guid Id,
    long Version,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    DateTimeOffset RecordedAt)
{
    public string Money => BalanceAfter.ToString("F2", CultureInfo.InvariantCulture);
}

internal sealed record SeededAccount(Guid AccountId, IReadOnlyList<SeededEntry> Entries);

internal sealed record AuditRow(
    string EventType,
    string ClientId,
    Guid? AccountId,
    string CorrelationId,
    string Outcome,
    string Details);
