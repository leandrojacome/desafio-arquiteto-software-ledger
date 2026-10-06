using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Idempotency;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Persistence.Outbox;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class IdempotencyKeyPruneTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task Prune_RemovesKeysOlderThanThirtyFiveDaysAndKeepsTheRest_LeavingTheLedgerUntouched()
    {
        await using var scenario = await PruneScenario.CreateAsync(postgres, batchSize: 100);
        var account = await scenario.Ledger.SeedAsync();
        var entry = account.Entries[0].Id;

        await scenario.InsertKeysAsync(account.AccountId, entry, "old-", 5, TimeSpan.FromDays(36));
        await scenario.InsertKeysAsync(account.AccountId, entry, "fresh-", 5, TimeSpan.FromDays(34));

        var before = await scenario.Ledger.FingerprintAsync();
        var removed = await scenario.Pruner.HandleAsync(CancellationToken.None);
        var after = await scenario.Ledger.FingerprintAsync();

        removed.ShouldBe(5);
        (await scenario.KeysAsync()).ShouldBe(
            Enumerable.Range(0, 5).Select(index => $"fresh-{index}"),
            ignoreOrder: true);
        after.ShouldBe(before);
        (await scenario.EntryCountAsync()).ShouldBe(account.Entries.Count);
    }

    [DockerFact]
    public async Task Prune_RepeatsPartialBatchesUntilNothingExpiredIsLeft()
    {
        await using var scenario = await PruneScenario.CreateAsync(postgres, batchSize: 100);
        var account = await scenario.Ledger.SeedAsync();

        await scenario.InsertKeysAsync(account.AccountId, account.Entries[0].Id, "old-", 250, TimeSpan.FromDays(40));
        await scenario.InsertKeysAsync(account.AccountId, account.Entries[0].Id, "fresh-", 7, TimeSpan.FromDays(1));

        var removed = await scenario.Pruner.HandleAsync(CancellationToken.None);

        removed.ShouldBe(250);
        (await scenario.KeysAsync()).Count.ShouldBe(7);
        (await scenario.Pruner.HandleAsync(CancellationToken.None)).ShouldBe(0);
    }

    [DockerFact]
    public async Task Prune_WithAnEmptyTable_RemovesNothing()
    {
        await using var scenario = await PruneScenario.CreateAsync(postgres, batchSize: 100);

        (await scenario.Pruner.HandleAsync(CancellationToken.None)).ShouldBe(0);
    }

    [DockerFact]
    public async Task TheWorkerRole_HasDeleteAndSelectOnlyOnTheKeyColumns_AndNoOtherWriteOnTheKeys()
    {
        await using var scenario = await PruneScenario.CreateAsync(postgres, batchSize: 100);

        await using var admin = await scenario.Database.OpenAdministrativeConnectionAsync(CancellationToken.None);

        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_worker', 'idempotency_keys', 'DELETE')")).ShouldBe(true);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_worker', 'idempotency_keys', 'INSERT')")).ShouldBe(false);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_worker', 'idempotency_keys', 'UPDATE')")).ShouldBe(false);
        (await ScalarAsync(admin, "SELECT has_column_privilege('ledger_worker', 'idempotency_keys', 'created_at', 'SELECT')")).ShouldBe(true);
        (await ScalarAsync(admin, "SELECT has_column_privilege('ledger_worker', 'idempotency_keys', 'account_id', 'SELECT')")).ShouldBe(true);
        (await ScalarAsync(admin, "SELECT has_column_privilege('ledger_worker', 'idempotency_keys', 'idempotency_key', 'SELECT')")).ShouldBe(true);
        (await ScalarAsync(admin, "SELECT has_column_privilege('ledger_worker', 'idempotency_keys', 'request_hash', 'SELECT')")).ShouldBe(false);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_api', 'idempotency_keys', 'DELETE')")).ShouldBe(false);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_readonly', 'idempotency_keys', 'DELETE')")).ShouldBe(false);
    }

    [DockerFact]
    public async Task TheIndexOnCreatedAt_ExistsAndServesThePruneQuery()
    {
        await using var scenario = await PruneScenario.CreateAsync(postgres, batchSize: 100);

        await using var admin = await scenario.Database.OpenAdministrativeConnectionAsync(CancellationToken.None);

        (await ScalarAsync(
            admin,
            "SELECT count(*) FROM pg_indexes WHERE tablename = 'idempotency_keys' AND indexname = 'ix_idempotency_keys_created_at'"))
            .ShouldBe(1L);
    }

    [SuppressMessage("Security", "CA2100", Justification = "Callers pass literal SQL.")]
    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        return await command.ExecuteScalarAsync(CancellationToken.None);
    }

    private sealed class PruneScenario : IAsyncDisposable
    {
        private readonly PostgresConnectionFactory _factory;

        private PruneScenario(EmptyDatabase database, PostgresConnectionFactory factory, int batchSize)
        {
            Database = database;
            _factory = factory;
            Ledger = new IntegrityLedger(database);
            Pruner = new PruneIdempotencyKeysHandler(
                new PostgresIdempotencyKeyPruner(factory),
                new IdempotencyPruneOptions { RetentionDays = 35, PruneBatchSize = batchSize }.ToSettings());
        }

        public EmptyDatabase Database { get; }

        public IntegrityLedger Ledger { get; }

        public PruneIdempotencyKeysHandler Pruner { get; }

        [SuppressMessage("Reliability", "CA2000",
            Justification = "Ownership of the database and the factory passes to the scenario, which disposes both.")]
        public static async Task<PruneScenario> CreateAsync(PostgresFixture postgres, int batchSize)
        {
            var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);

            (await database.MigrateAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();

            return new PruneScenario(database, PostgresFixture.CreateConnectionFactory(database.Settings), batchSize);
        }

        public async Task InsertKeysAsync(Guid accountId, Guid entryId, string prefix, int count, TimeSpan age)
        {
            await using var connection = await Database.OpenAdministrativeConnectionAsync(CancellationToken.None);
            await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

            for (var index = 0; index < count; index++)
            {
                await using var command = new NpgsqlCommand(
                    """
                    INSERT INTO idempotency_keys (account_id, idempotency_key, request_hash, hash_version, entry_id, created_at)
                    VALUES (@account_id, @key, decode(repeat('ab', 32), 'hex'), 1, @entry_id,
                            clock_timestamp() - make_interval(secs => @age_seconds))
                    """,
                    connection,
                    transaction);

                command.Parameters.Add("account_id", NpgsqlDbType.Uuid).Value = accountId;
                command.Parameters.Add("key", NpgsqlDbType.Text).Value = $"{prefix}{index}";
                command.Parameters.Add("entry_id", NpgsqlDbType.Uuid).Value = entryId;
                command.Parameters.Add("age_seconds", NpgsqlDbType.Double).Value = age.TotalSeconds;

                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await transaction.CommitAsync(CancellationToken.None);
        }

        public async Task<List<string>> KeysAsync()
        {
            await using var connection = await Database.OpenAdministrativeConnectionAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand("SELECT idempotency_key FROM idempotency_keys", connection);
            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

            var keys = new List<string>();

            while (await reader.ReadAsync(CancellationToken.None))
            {
                keys.Add(reader.GetString(0));
            }

            return keys;
        }

        public async Task<long> EntryCountAsync()
        {
            await using var connection = await Database.OpenAdministrativeConnectionAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand("SELECT count(*) FROM ledger_entries", connection);

            return (long)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0L);
        }

        public async ValueTask DisposeAsync()
        {
            await _factory.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}
