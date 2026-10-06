using Ledger.Api.IntegrationTests.Infrastructure;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Api.IntegrationTests.Consumers;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class GapRecoveryTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task ReadingTheStatementFillsTheGaps_AndTheRebuiltBalanceEqualsTheLastEventBalance()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var consumer = await ReferenceConsumer.StartAsync(broker, scenario.Database, UniqueQueue());
        var ledger = new IntegrityLedger(scenario.Database);
        var account = await ledger.SeedAsync();

        foreach (var entry in account.Entries.Where(entry => entry.Version is 1 or 2 or 5))
        {
            await RawEventPublisher.PublishAsync(
                broker,
                Guid.NewGuid(),
                RawEventPublisher.EventJson(account.AccountId, entry.Version, entry.BalanceAfter));
        }

        await ConditionWait.UntilAsync(() => consumer.Applied == 3, Patience, "the three events to be applied");

        var gaps = await consumer.GapsOfAsync(account.AccountId);

        gaps.ShouldBe([3L, 4L]);

        var statement = await ReadStatementAsync(scenario.Database, account.AccountId, gaps);
        var state = await consumer.StateOfAsync(account.AccountId);

        statement.Select(row => row.Version).ShouldBe(gaps);
        state.ShouldNotBeNull();
        state.Value.Balance.ShouldBe(account.Entries[^1].BalanceAfter);
        statement[^1].BalanceAfter.ShouldBe(account.Entries[3].BalanceAfter);
        (await LastBalanceOfStatementAsync(scenario.Database, account.AccountId)).ShouldBe(state.Value.Balance);
    }

    private static async Task<IReadOnlyList<(long Version, decimal BalanceAfter)>> ReadStatementAsync(
        EmptyDatabase database,
        Guid accountId,
        IReadOnlyList<long> versions)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            """
            SELECT account_version, balance_after
            FROM ledger_entries
            WHERE account_id = @id AND account_version = ANY(@versions)
            ORDER BY account_version
            """,
            connection);

        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = accountId;
        command.Parameters.Add("versions", NpgsqlDbType.Array | NpgsqlDbType.Bigint).Value = versions.ToArray();

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        var rows = new List<(long, decimal)>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            rows.Add((reader.GetInt64(0), reader.GetDecimal(1)));
        }

        return rows;
    }

    private static async Task<decimal> LastBalanceOfStatementAsync(EmptyDatabase database, Guid accountId)
    {
        await using var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            "SELECT balance_after FROM ledger_entries WHERE account_id = @id ORDER BY account_version DESC LIMIT 1",
            connection);

        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = accountId;

        return (decimal)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0m);
    }

    private static string UniqueQueue() => $"reference-consumer.{Guid.NewGuid():N}.ledger.entry-registered";
}
