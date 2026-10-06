using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Consumers;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class ConsumerConvergenceTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private const int Accounts = 20;
    private const int EventsPerAccount = 50;
    private const int Total = Accounts * EventsPerAccount;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(120);

    [DockerFact]
    public async Task ThousandEventsShuffledAndTwentyPercentRepublished_ConvergeToTheSameStatePerAccount()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var consumer = await ReferenceConsumer.StartAsync(
            broker,
            scenario.Database,
            $"reference-consumer.{Guid.NewGuid():N}.ledger.entry-registered");

        var accounts = Enumerable.Range(0, Accounts).Select(_ => Guid.NewGuid()).ToList();
        var seeds = ShuffledSeeds(accounts);
        var ids = await scenario.Seeder.InsertEventsAsync(seeds);

        scenario.StartWorker();

        await ConditionWait.UntilAsync(
            () => consumer.Applied + consumer.Ignored == Total,
            Patience,
            "every event to be consumed once");
        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 0,
            Patience,
            "the outbox to drain");

        var republished = ids.Where((_, index) => index % 5 == 0).ToList();

        await scenario.Seeder.MarkPendingAgainAsync(republished);

        await ConditionWait.UntilAsync(
            () => consumer.Duplicates == republished.Count,
            Patience,
            "the republished events to be recognised as duplicates");

        (await consumer.ProcessedCountAsync()).ShouldBe(Total);
        consumer.Rejected.ShouldBe(0);
        consumer.Applied.ShouldBeGreaterThan(0);

        foreach (var account in accounts)
        {
            (await consumer.StateOfAsync(account)).ShouldBe((EventsPerAccount, EventsPerAccount * 10m));
        }

        (await consumer.GapCountAsync()).ShouldBe(consumer.Ignored);
    }

    private static List<OutboxEventSeed> ShuffledSeeds(List<Guid> accounts)
    {
        var seeds = new List<OutboxEventSeed>(Total);

        for (var account = 0; account < accounts.Count; account++)
        {
            for (var version = 1; version <= EventsPerAccount; version++)
            {
                var position = ((account * EventsPerAccount) + version) * 7919 % 100_003;

                seeds.Add(new OutboxEventSeed(
                    accounts[account],
                    version,
                    version * 10m,
                    TimeSpan.FromMilliseconds(position)));
            }
        }

        return seeds;
    }
}
