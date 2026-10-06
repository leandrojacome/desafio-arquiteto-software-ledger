using Ledger.Api.IntegrationTests.Infrastructure;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class IdempotencyPruneServiceTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task TheWorker_PrunesTheExpiredKeysThroughItsOwnRole_AndLeavesTheLedgerAndTheFreshKeys()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var ledger = new IntegrityLedger(scenario.Database);
        var account = await ledger.SeedAsync();

        await InsertKeysAsync(scenario.Database, account.AccountId, account.Entries[0].Id, "expired-", 130, TimeSpan.FromDays(36));
        await InsertKeysAsync(scenario.Database, account.AccountId, account.Entries[0].Id, "valid-", 4, TimeSpan.FromDays(34));

        var before = await ledger.FingerprintAsync();

        scenario.StartWorker(new Dictionary<string, string?>
        {
            ["Idempotency:PruneBatchSize"] = "100",
            ["RabbitMq:Port"] = "1"
        });

        await ConditionWait.UntilAsync(
            async () => await CountKeysAsync(scenario.Database) == 4,
            Patience,
            "the expired keys to be pruned");

        (await ledger.FingerprintAsync()).ShouldBe(before);
    }

    private static async Task InsertKeysAsync(
        EmptyDatabase database,
        Guid accountId,
        Guid entryId,
        string prefix,
        int count,
        TimeSpan age)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);
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

    private static async Task<long> CountKeysAsync(EmptyDatabase database)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM idempotency_keys", connection);

        return (long)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0L);
    }
}
