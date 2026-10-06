using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence.Support;

internal sealed class LedgerSeeder(PostgresFixture postgres)
{
    private const int ChainCommandTimeoutSeconds = 180;

    public static async Task SeedChainAsync(
        NpgsqlConnection connection,
        AccountId accountId,
        int count,
        DateTimeOffset start,
        TimeSpan step,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        await using (var account = new NpgsqlCommand(
                         """
                         WITH new_account AS (
                             INSERT INTO accounts (id, currency) VALUES (@account_id, 'BRL') RETURNING id
                         )
                         INSERT INTO account_balances (account_id, balance, overdraft_limit, version, last_recorded_at)
                         SELECT id, 0, 0, 0, @start FROM new_account
                         """,
                         connection))
        {
            account.Parameters.AddWithValue("account_id", accountId.Value);
            account.Parameters.AddWithValue("start", start.UtcDateTime);

            await account.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var entries = new NpgsqlCommand(
                         """
                         INSERT INTO ledger_entries (id, account_id, account_version, type, amount, currency, balance_after,
                                                     recorded_at, occurred_at, client_id, correlation_id)
                         SELECT gen_random_uuid(), @account_id, chain.version, chain.type, chain.amount, 'BRL',
                                SUM(CASE WHEN chain.type = 'CREDIT' THEN chain.amount ELSE -chain.amount END)
                                    OVER (ORDER BY chain.version),
                                chain.moment, chain.moment, 'seeder', 'seeder'
                         FROM (
                             SELECT series.version,
                                    CASE WHEN series.version % 3 = 0 THEN 'DEBIT' ELSE 'CREDIT' END AS type,
                                    (1 + series.version % 5)::numeric(18,2) AS amount,
                                    @start + (series.version - 1) * make_interval(secs => @step_seconds) AS moment
                             FROM generate_series(1, @count) AS series(version)
                         ) AS chain
                         """,
                         connection))
        {
            entries.CommandTimeout = ChainCommandTimeoutSeconds;
            entries.Parameters.AddWithValue("account_id", accountId.Value);
            entries.Parameters.AddWithValue("start", start.UtcDateTime);
            entries.Parameters.AddWithValue("step_seconds", step.TotalSeconds);
            entries.Parameters.AddWithValue("count", count);

            await entries.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var settle = new NpgsqlCommand(
            """
            UPDATE account_balances AS ab
            SET balance = last.balance_after, version = last.account_version,
                last_entry_id = last.id, last_recorded_at = last.recorded_at
            FROM (
                SELECT id, account_version, balance_after, recorded_at
                FROM ledger_entries
                WHERE account_id = @account_id
                ORDER BY account_version DESC
                LIMIT 1
            ) AS last
            WHERE ab.account_id = @account_id
            """,
            connection);

        settle.Parameters.AddWithValue("account_id", accountId.Value);

        await settle.ExecuteNonQueryAsync(cancellationToken);
    }

    [SuppressMessage("Maintainability", "CA1508",
        Justification = "A null Guid? boxes to null; the analyzer misreads the cast of the nullable value.")]
    public async Task<Guid> InsertAtAsync(
        AccountId accountId,
        long version,
        string type,
        decimal amount,
        decimal balanceAfter,
        DateTimeOffset recordedAt,
        DateTimeOffset? occurredAt = null,
        string? description = null,
        string? reference = null,
        Guid? reversesEntryId = null,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.CreateVersion7();

        await using var command = postgres.AdministrativeSource.CreateCommand(
            """
            INSERT INTO ledger_entries (id, account_id, account_version, type, amount, currency, balance_after,
                                        recorded_at, occurred_at, description, reference, reverses_entry_id,
                                        client_id, correlation_id)
            VALUES (@id, @account_id, @version, @type, @amount, 'BRL', @balance_after,
                    @recorded_at, @occurred_at, @description, @reference, @reverses_entry_id,
                    'seeder', 'seeder')
            """);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("account_id", accountId.Value);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("balance_after", balanceAfter);
        command.Parameters.AddWithValue("recorded_at", recordedAt.UtcDateTime);
        command.Parameters.AddWithValue("occurred_at", (occurredAt ?? recordedAt).UtcDateTime);
        command.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        command.Parameters.AddWithValue("reference", (object?)reference ?? DBNull.Value);
        command.Parameters.AddWithValue("reverses_entry_id", (object?)reversesEntryId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return id;
    }
}
