using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Reads;

internal sealed class ReadLedger(PostgresFixture postgres)
{
    private const string InsertChainSql = """
                                          INSERT INTO ledger_entries (id, account_id, account_version, type, amount, currency, balance_after,
                                                                      recorded_at, occurred_at, description, reference, reverses_entry_id,
                                                                      client_id, correlation_id)
                                          SELECT gen_random_uuid(), @account_id, s.n, s.type, s.amount, 'BRL',
                                                 SUM(CASE WHEN s.type = 'CREDIT' THEN s.amount ELSE -s.amount END) OVER (ORDER BY s.n),
                                                 @start + (((s.n - 1) / @per_instant) * @step_microseconds) * interval '1 microsecond',
                                                 @start + (((s.n - 1) / @per_instant) * @step_microseconds) * interval '1 microsecond',
                                                 NULL, NULL, NULL, 'seeder', 'seeder'
                                          FROM (SELECT n, CASE WHEN n % 4 = 0 THEN 'DEBIT' ELSE 'CREDIT' END AS type, 10.00 AS amount
                                                FROM generate_series(1, @count) AS n) AS s
                                          """;

    private const string SyncBalanceSql = """
                                          UPDATE account_balances AS b
                                          SET balance = e.balance_after,
                                              version = e.account_version,
                                              last_entry_id = e.id,
                                              last_recorded_at = e.recorded_at
                                          FROM (SELECT id, account_version, balance_after, recorded_at
                                                FROM ledger_entries
                                                WHERE account_id = @account_id
                                                ORDER BY account_version DESC
                                                LIMIT 1) AS e
                                          WHERE b.account_id = @account_id
                                          """;

    public async Task SeedChainAsync(
        AccountId accountId,
        int count,
        DateTimeOffset start,
        TimeSpan step,
        int entriesPerInstant = 1)
    {
        await using (var insert = postgres.AdministrativeSource.CreateCommand(InsertChainSql))
        {
            insert.Parameters.AddWithValue("account_id", accountId.Value);
            insert.Parameters.AddWithValue("count", count);
            insert.Parameters.AddWithValue("per_instant", entriesPerInstant);
            insert.Parameters.AddWithValue("step_microseconds", step.Ticks / 10);
            insert.Parameters.AddWithValue("start", start.UtcDateTime);

            await insert.ExecuteNonQueryAsync();
        }

        await using var sync = postgres.AdministrativeSource.CreateCommand(SyncBalanceSql);

        sync.Parameters.AddWithValue("account_id", accountId.Value);

        await sync.ExecuteNonQueryAsync();
    }

    public async Task SetOverdraftLimitAsync(AccountId accountId, decimal limit)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "UPDATE account_balances SET overdraft_limit = @limit WHERE account_id = @account_id");

        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("account_id", accountId.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task SetCurrentBalanceAsync(
        AccountId accountId,
        decimal balance,
        long version,
        Guid lastEntryId,
        DateTimeOffset lastRecordedAt)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            """
            UPDATE account_balances
            SET balance = @balance, version = @version, last_entry_id = @last_entry_id, last_recorded_at = @last_recorded_at
            WHERE account_id = @account_id
            """);

        command.Parameters.AddWithValue("balance", balance);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("last_entry_id", lastEntryId);
        command.Parameters.AddWithValue("last_recorded_at", lastRecordedAt.UtcDateTime);
        command.Parameters.AddWithValue("account_id", accountId.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<InFlightEntry> OpenInFlightEntryAsync(AccountId accountId, TimeSpan age, decimal amount)
    {
        var connection = await postgres.AdministrativeSource.OpenConnectionAsync();
        var transaction = await connection.BeginTransactionAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO ledger_entries (id, account_id, account_version, type, amount, currency, balance_after,
                                        recorded_at, occurred_at, client_id, correlation_id)
            VALUES (@id, @account_id, 1, 'CREDIT', @amount, 'BRL', @amount,
                    clock_timestamp() - @age, clock_timestamp() - @age, 'seeder', 'seeder')
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("account_id", accountId.Value);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("age", age);

        await command.ExecuteNonQueryAsync();

        return new InFlightEntry(connection, transaction);
    }

    internal sealed class InFlightEntry(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        private bool _committed;

        public async Task CommitAsync()
        {
            await transaction.CommitAsync();
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                await transaction.RollbackAsync();
            }

            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
