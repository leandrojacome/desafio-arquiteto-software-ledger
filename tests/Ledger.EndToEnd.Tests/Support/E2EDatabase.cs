using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Npgsql;

namespace Ledger.EndToEnd.Tests.Support;

internal sealed record StoredEntry(
    long AccountVersion,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    Guid? ReversesEntryId,
    Guid Id);

internal sealed record IntegrityRun(
    DateTimeOffset RecordedAt,
    string Outcome,
    string Mode,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    bool Partial,
    long Violations);

internal sealed record IntegrityViolation(string Check, string Expected, string Found);

internal sealed class E2EDatabase(string connectionString)
{
    public async Task<long> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = Command(connection, sql, parameters);

        var value = await command.ExecuteScalarAsync(CancellationToken.None);

        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public Task<long> CountEntriesAsync(string accountId) =>
        ScalarAsync("SELECT count(*) FROM ledger_entries WHERE account_id = @id", ("id", Guid.Parse(accountId)));

    public Task<long> PendingOutboxAsync(string accountId) =>
        ScalarAsync(
            "SELECT count(*) FROM outbox_messages WHERE account_id = @id AND published_at IS NULL",
            ("id", Guid.Parse(accountId)));

    public Task<long> OutboxCountAsync(string accountId) =>
        ScalarAsync("SELECT count(*) FROM outbox_messages WHERE account_id = @id", ("id", Guid.Parse(accountId)));

    public Task<long> SchemaVersionCountAsync() => ScalarAsync("SELECT count(*) FROM schemaversions");

    public async Task<decimal> StoredBalanceAsync(string accountId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = Command(
            connection,
            "SELECT balance FROM account_balances WHERE account_id = @id",
            [("id", Guid.Parse(accountId))]);

        var value = await command.ExecuteScalarAsync(CancellationToken.None);

        return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    public async Task<byte[]> EncryptedDocumentAsync(string accountId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = Command(
            connection,
            "SELECT holder_document_encrypted FROM accounts WHERE id = @id",
            [("id", Guid.Parse(accountId))]);

        var value = await command.ExecuteScalarAsync(CancellationToken.None);

        return value as byte[] ?? throw new InvalidOperationException("The account has no encrypted holder document.");
    }

    public async Task<IReadOnlyList<StoredEntry>> EntriesAsync(string accountId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = Command(
            connection,
            "SELECT id, account_version, type, amount, balance_after, reverses_entry_id FROM ledger_entries WHERE account_id = @id ORDER BY account_version",
            [("id", Guid.Parse(accountId))]);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var entries = new List<StoredEntry>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            entries.Add(new StoredEntry(
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                await reader.IsDBNullAsync(5, CancellationToken.None) ? null : reader.GetGuid(5),
                reader.GetGuid(0)));
        }

        return entries;
    }

    public async Task<IReadOnlyList<IntegrityRun>> IntegrityRunsAsync(DateTimeOffset since)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = Command(
            connection,
            "SELECT recorded_at, outcome, details::text FROM audit_log WHERE event_type = 'integrity.run_completed' AND recorded_at > @since ORDER BY id",
            [("since", since.ToUniversalTime())]);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var runs = new List<IntegrityRun>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            using var details = System.Text.Json.JsonDocument.Parse(reader.GetString(2));
            var root = details.RootElement;

            runs.Add(new IntegrityRun(
                await reader.GetFieldValueAsync<DateTimeOffset>(0, CancellationToken.None),
                reader.GetString(1),
                root.GetProperty("mode").GetString() ?? string.Empty,
                ParseInstant(root.GetProperty("windowStart").GetString()),
                ParseInstant(root.GetProperty("windowEnd").GetString()),
                root.TryGetProperty("partial", out var partial) && partial.GetBoolean(),
                root.GetProperty("violations").GetInt64()));
        }

        return runs;
    }

    public async Task<IReadOnlyList<IntegrityViolation>> IntegrityViolationsAsync(string accountId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = Command(
            connection,
            "SELECT details::text FROM audit_log WHERE event_type = 'integrity.violation_detected' AND account_id = @id ORDER BY id",
            [("id", Guid.Parse(accountId))]);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var violations = new List<IntegrityViolation>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            using var details = System.Text.Json.JsonDocument.Parse(reader.GetString(0));
            var root = details.RootElement;

            violations.Add(new IntegrityViolation(
                root.GetProperty("check").GetString() ?? string.Empty,
                root.GetProperty("expected").GetString() ?? string.Empty,
                root.GetProperty("found").GetString() ?? string.Empty));
        }

        return violations;
    }

    public async Task ShiftStoredBalanceAsync(string accountId, decimal delta)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = Command(
            connection,
            "UPDATE account_balances SET balance = balance + @delta WHERE account_id = @id",
            [("delta", delta), ("id", Guid.Parse(accountId))]);

        (await command.ExecuteNonQueryAsync(CancellationToken.None)).ShouldBe(1);
    }

    public async Task<IReadOnlyList<string>> InvariantViolationsAsync(string accountId)
    {
        var entries = await EntriesAsync(accountId);
        var stored = await StoredBalanceAsync(accountId);
        var violations = new List<string>();
        var running = 0m;
        var expectedVersion = 1L;

        foreach (var entry in entries)
        {
            running += entry.Type == "CREDIT" ? entry.Amount : -entry.Amount;

            if (entry.AccountVersion != expectedVersion)
            {
                violations.Add($"version {entry.AccountVersion} found where {expectedVersion} was expected");
                expectedVersion = entry.AccountVersion;
            }

            if (entry.BalanceAfter != running)
            {
                violations.Add($"version {entry.AccountVersion} has balance_after {entry.BalanceAfter}, the chain says {running}");
            }

            expectedVersion++;
        }

        if (stored != running)
        {
            violations.Add($"account_balances holds {stored} and the entries add up to {running}");
        }

        return violations;
    }

    private static DateTimeOffset ParseInstant(string? text) =>
        DateTimeOffset.Parse(text ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    [SuppressMessage("Security", "CA2100", Justification = "The statements are constants of this test helper; only values travel as parameters.")]
    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }
}
