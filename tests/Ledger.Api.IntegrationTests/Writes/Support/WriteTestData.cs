using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Api.IntegrationTests.Writes.Support;

internal sealed class WriteTestData(PostgresFixture postgres)
{
    public LedgerQueries Queries { get; } = new(postgres);

    public static AccountId Account(string text) => AccountId.From(text).Value;

    public async Task AssertConsistentAsync(string accountId)
    {
        await InvariantVerifier.AssertAccountIsConsistentAsync(
            postgres.AdministrativeSource,
            Account(accountId).Value,
            CancellationToken.None);
    }

    public async Task<long> CountKeysAsync(string accountId) =>
        await Queries.CountKeysAsync(Account(accountId), CancellationToken.None);

    public async Task<long> CountEntriesAsync(string accountId) =>
        await Queries.CountEntriesAsync(Account(accountId), CancellationToken.None);

    public async Task<long> CountOutboxAsync(string accountId) =>
        await Queries.CountOutboxAsync(Account(accountId), CancellationToken.None);

    public async Task<decimal> BalanceAsync(string accountId) =>
        (await Queries.BalanceRowAsync(Account(accountId), CancellationToken.None)).Balance;

    public async Task<(bool IsFinite, string Utc)> LastOccurredAtAsync(string accountId)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            """
            SELECT isfinite(occurred_at), (occurred_at AT TIME ZONE 'UTC')::text
            FROM ledger_entries
            WHERE account_id = @account_id
            ORDER BY account_version DESC
            LIMIT 1
            """);

        command.Parameters.AddWithValue("account_id", Account(accountId).Value);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();

        return (reader.GetBoolean(0), reader.GetString(1));
    }

    public async Task<IReadOnlyList<string>> OutboxPayloadsAsync(string accountId)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT payload::text FROM outbox_messages WHERE account_id = @account_id ORDER BY created_at, id");

        command.Parameters.AddWithValue("account_id", Account(accountId).Value);

        var payloads = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        while (await reader.ReadAsync(CancellationToken.None))
        {
            payloads.Add(reader.GetString(0));
        }

        return payloads;
    }

    public async Task<IReadOnlyList<AuditRow>> AuditRowsAsync(string accountId, string eventType)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            """
            SELECT client_id, correlation_id, outcome, details::text
            FROM audit_log
            WHERE account_id = @account_id AND event_type = @event_type
            """);

        command.Parameters.AddWithValue("account_id", Account(accountId).Value);
        command.Parameters.AddWithValue("event_type", eventType);

        var rows = new List<AuditRow>();

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        while (await reader.ReadAsync(CancellationToken.None))
        {
            rows.Add(new AuditRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return rows;
    }

    public async Task<StoredAccount> StoredAccountAsync(string accountId)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            """
            SELECT a.holder_document_encrypted, a.holder_document_blind_index, a.holder_document_key_version,
                   b.balance, b.overdraft_limit, b.version, a.currency
            FROM accounts a
            JOIN account_balances b ON b.account_id = a.id
            WHERE a.id = @id
            """);

        command.Parameters.AddWithValue("id", Account(accountId).Value);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();

        return new StoredAccount(
            (byte[])reader.GetValue(0),
            (byte[])reader.GetValue(1),
            reader.GetInt32(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetInt64(5),
            reader.GetString(6));
    }

    public async Task<string> EverythingStoredAsTextAsync()
    {
        var builder = new System.Text.StringBuilder();

        foreach (var table in new[] { "accounts", "account_balances", "ledger_entries", "idempotency_keys", "account_creation_keys", "outbox_messages", "audit_log" })
        {
            await using var command = postgres.AdministrativeSource.CreateCommand(
                $"SELECT t::text FROM {table} t");

            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

            while (await reader.ReadAsync(CancellationToken.None))
            {
                builder.AppendLine(reader.GetString(0));
            }
        }

        return builder.ToString();
    }

    public async Task<long> CountAccountsAsync()
    {
        await using var command = postgres.AdministrativeSource.CreateCommand("SELECT count(*) FROM accounts");

        return (long)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0L);
    }

    public async Task<long> CountAuditAsync(string eventType)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT count(*) FROM audit_log WHERE event_type = @event_type");

        command.Parameters.AddWithValue("event_type", eventType);

        return (long)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0L);
    }
}

internal sealed class ThrowingUnitOfWork(Exception failure) : IUnitOfWork
{
    public Task<Result<TValue>> ExecuteAsync<TValue>(
        Func<IUnitOfWorkScope, CancellationToken, Task<Result<TValue>>> work,
        CancellationToken cancellationToken)
        where TValue : notnull => Task.FromException<Result<TValue>>(failure);
}

internal sealed record AuditRow(string ClientId, string CorrelationId, string Outcome, string Details);

internal sealed record StoredAccount(
    byte[] Encrypted,
    byte[] BlindIndex,
    int KeyVersion,
    decimal Balance,
    decimal OverdraftLimit,
    long Version,
    string Currency);
