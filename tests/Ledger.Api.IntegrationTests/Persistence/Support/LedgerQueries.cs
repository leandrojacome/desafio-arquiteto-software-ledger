using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence.Support;

internal sealed class LedgerQueries(PostgresFixture postgres)
{
    public async Task<BalanceRow> BalanceRowAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT balance, version, last_entry_id, last_recorded_at, overdraft_limit FROM account_balances WHERE account_id = @account_id");

        command.Parameters.AddWithValue("account_id", accountId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue();

        return new BalanceRow(
            reader.GetDecimal(0),
            reader.GetInt64(1),
            await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetGuid(2),
            reader.GetDateTime(3),
            reader.GetDecimal(4));
    }

    public async Task<IReadOnlyList<EntryRow>> EntriesAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            """
            SELECT id, account_version, type, amount, currency, balance_after, recorded_at, occurred_at,
                   description, reference, reverses_entry_id, client_id, correlation_id
            FROM ledger_entries
            WHERE account_id = @account_id
            ORDER BY account_version
            """);

        command.Parameters.AddWithValue("account_id", accountId.Value);

        var rows = new List<EntryRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new EntryRow(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetString(4),
                reader.GetDecimal(5),
                reader.GetDateTime(6),
                reader.GetDateTime(7),
                await reader.IsDBNullAsync(8, cancellationToken) ? null : reader.GetString(8),
                await reader.IsDBNullAsync(9, cancellationToken) ? null : reader.GetString(9),
                await reader.IsDBNullAsync(10, cancellationToken) ? null : reader.GetGuid(10),
                reader.GetString(11),
                reader.GetString(12)));
        }

        return rows;
    }

    public async Task<OutboxRow> OutboxRowAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            """
            SELECT id, account_id, type, payload->>'entryId', correlation_id, traceparent, created_at, published_at, attempts
            FROM outbox_messages
            WHERE id = @id
            """);

        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        (await reader.ReadAsync(cancellationToken)).ShouldBeTrue();

        return new OutboxRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetString(3),
            reader.GetString(4),
            await reader.IsDBNullAsync(5, cancellationToken) ? null : reader.GetString(5),
            reader.GetDateTime(6),
            await reader.IsDBNullAsync(7, cancellationToken) ? null : reader.GetDateTime(7),
            reader.GetInt32(8));
    }

    public async Task<long> BalanceRowCountAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT count(*) FROM account_balances WHERE account_id = @account_id");

        command.Parameters.AddWithValue("account_id", accountId.Value);

        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public Task<long> CountEntriesAsync(AccountId accountId, CancellationToken cancellationToken = default) =>
        CountAsync("ledger_entries", accountId, cancellationToken);

    public Task<long> CountKeysAsync(AccountId accountId, CancellationToken cancellationToken = default) =>
        CountAsync("idempotency_keys", accountId, cancellationToken);

    public Task<long> CountOutboxAsync(AccountId accountId, CancellationToken cancellationToken = default) =>
        CountAsync("outbox_messages", accountId, cancellationToken);

    public async Task<long> CountAuditAsync(AccountId accountId, string eventType, CancellationToken cancellationToken = default)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT count(*) FROM audit_log WHERE account_id = @account_id AND event_type = @event_type");

        command.Parameters.AddWithValue("account_id", accountId.Value);
        command.Parameters.AddWithValue("event_type", eventType);

        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async Task PushLastRecordedAtAheadAsync(AccountId accountId, TimeSpan amount, CancellationToken cancellationToken = default)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "UPDATE account_balances SET last_recorded_at = clock_timestamp() + @amount WHERE account_id = @account_id");

        command.Parameters.AddWithValue("account_id", accountId.Value);
        command.Parameters.AddWithValue("amount", amount);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DateTime> DatabaseNowAsync(CancellationToken cancellationToken = default)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand("SELECT clock_timestamp()");

        return (DateTime)(await command.ExecuteScalarAsync(cancellationToken) ?? default(DateTime));
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "The table name is one of three constants of this class, never input.")]
    private async Task<long> CountAsync(string table, AccountId accountId, CancellationToken cancellationToken)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            $"SELECT count(*) FROM {table} WHERE account_id = @account_id");

        command.Parameters.AddWithValue("account_id", accountId.Value);

        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    internal sealed record BalanceRow(
        decimal Balance,
        long Version,
        Guid? LastEntryId,
        DateTime LastRecordedAt,
        decimal OverdraftLimit);

    internal sealed record EntryRow(
        Guid Id,
        long AccountVersion,
        string Type,
        decimal Amount,
        string Currency,
        decimal BalanceAfter,
        DateTime RecordedAt,
        DateTime OccurredAt,
        string? Description,
        string? Reference,
        Guid? ReversesEntryId,
        string ClientId,
        string CorrelationId);

    internal sealed record OutboxRow(
        Guid Id,
        Guid AccountId,
        string Type,
        string? PayloadEntryId,
        string CorrelationId,
        string? TraceParent,
        DateTime CreatedAt,
        DateTime? PublishedAt,
        int Attempts);
}
