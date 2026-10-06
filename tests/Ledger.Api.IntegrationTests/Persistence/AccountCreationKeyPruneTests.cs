using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Infrastructure.Persistence.Outbox;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class AccountCreationKeyPruneTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(35);

    [DockerFact]
    public async Task Prune_RemovesTheAccountCreationKeysOlderThanTheRetentionAndKeepsTheRest()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        (await database.MigrateAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();
        await using var factory = PostgresFixture.CreateConnectionFactory(database.Settings);
        await SeedAsync(database, "old-", 5, TimeSpan.FromDays(36));
        await SeedAsync(database, "fresh-", 3, TimeSpan.FromDays(34));
        var pruner = new PostgresIdempotencyKeyPruner(factory);

        var removed = await pruner.PruneAsync(Retention, 100, CancellationToken.None);

        removed.ShouldBe(5);
        (await KeysAsync(database)).ShouldBe(
            Enumerable.Range(0, 3).Select(index => $"fresh-{index}"),
            ignoreOrder: true);
        (await AccountCountAsync(database)).ShouldBe(8);
    }

    [DockerFact]
    public async Task Prune_RespectsTheBatchSizeForEachTableAndReportsBothCounts()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        (await database.MigrateAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();
        await using var factory = PostgresFixture.CreateConnectionFactory(database.Settings);
        await SeedAsync(database, "old-", 12, TimeSpan.FromDays(40));
        var pruner = new PostgresIdempotencyKeyPruner(factory);

        var first = await pruner.PruneAsync(Retention, 5, CancellationToken.None);
        var second = await pruner.PruneAsync(Retention, 5, CancellationToken.None);
        var third = await pruner.PruneAsync(Retention, 5, CancellationToken.None);
        var fourth = await pruner.PruneAsync(Retention, 5, CancellationToken.None);

        new[] { first, second, third, fourth }.ShouldBe([5, 5, 2, 0]);
        (await KeysAsync(database)).ShouldBeEmpty();
    }

    [DockerFact]
    public async Task Prune_WithNoAccountCreationKeys_RemovesNothing()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        (await database.MigrateAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();
        await using var factory = PostgresFixture.CreateConnectionFactory(database.Settings);

        var removed = await new PostgresIdempotencyKeyPruner(factory).PruneAsync(Retention, 100, CancellationToken.None);

        removed.ShouldBe(0);
    }

    private static async Task SeedAsync(EmptyDatabase database, string prefix, int count, TimeSpan age)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

        for (var index = 0; index < count; index++)
        {
            var accountId = Guid.CreateVersion7();

            await using var account = new NpgsqlCommand(
                "INSERT INTO accounts (id, currency) VALUES (@id, 'BRL')",
                connection,
                transaction);

            account.Parameters.Add("id", NpgsqlDbType.Uuid).Value = accountId;
            await account.ExecuteNonQueryAsync(CancellationToken.None);

            await using var key = new NpgsqlCommand(
                """
                INSERT INTO account_creation_keys (client_id, idempotency_key, account_id, request_hash, hash_version, created_at)
                VALUES ('pix-core', @key, @account_id, decode(repeat('cd', 32), 'hex'), 1,
                        clock_timestamp() - make_interval(secs => @age_seconds))
                """,
                connection,
                transaction);

            key.Parameters.Add("key", NpgsqlDbType.Text).Value = $"{prefix}{index}";
            key.Parameters.Add("account_id", NpgsqlDbType.Uuid).Value = accountId;
            key.Parameters.Add("age_seconds", NpgsqlDbType.Double).Value = age.TotalSeconds;
            await key.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await transaction.CommitAsync(CancellationToken.None);
    }

    private static async Task<List<string>> KeysAsync(EmptyDatabase database)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand("SELECT idempotency_key FROM account_creation_keys", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        var keys = new List<string>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    private static async Task<long> AccountCountAsync(EmptyDatabase database)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM accounts", connection);

        return (long)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0L);
    }
}
