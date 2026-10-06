using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Consumers;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class EventOrderingTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task AnOlderEventArrivingAfterANewerOne_IsIgnoredAndLeavesTheStateUntouched()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var consumer = await ReferenceConsumer.StartAsync(broker, scenario.Database, UniqueQueue());
        var account = Guid.NewGuid();

        await consumer.SeedStateAsync(account, 1842, 18_420.00m);

        await Publish(account, 1843, 18_430.00m);
        await Publish(account, 1844, 18_440.00m);
        await Publish(account, 1846, 18_460.00m);
        await Publish(account, 1845, 18_450.00m);

        await ConditionWait.UntilAsync(
            () => consumer.Applied + consumer.Ignored == 4,
            Patience,
            "the consumer to process the four events");

        consumer.Applied.ShouldBe(3);
        consumer.Ignored.ShouldBe(1);
        (await consumer.StateOfAsync(account)).ShouldBe((1846L, 18_460.00m));
    }

    [DockerFact]
    public async Task AnEventThatSkipsVersions_IsAppliedByStateAndTheGapsAreRecorded()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var consumer = await ReferenceConsumer.StartAsync(broker, scenario.Database, UniqueQueue());
        var account = Guid.NewGuid();

        await consumer.SeedStateAsync(account, 1843, 18_430.00m);
        await Publish(account, 1846, 18_460.00m);

        await ConditionWait.UntilAsync(() => consumer.Applied == 1, Patience, "the consumer to apply the event");

        (await consumer.GapsOfAsync(account)).ShouldBe([1844L, 1845L]);
        (await consumer.StateOfAsync(account)).ShouldBe((1846L, 18_460.00m));
    }

    [DockerFact]
    public async Task ASequenceInOrder_RecordsNoGap()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var consumer = await ReferenceConsumer.StartAsync(broker, scenario.Database, UniqueQueue());
        var account = Guid.NewGuid();

        for (var version = 1; version <= 5; version++)
        {
            await Publish(account, version, version * 10m);
        }

        await ConditionWait.UntilAsync(() => consumer.Applied == 5, Patience, "the five events to be applied");

        (await consumer.GapCountAsync()).ShouldBe(0);
        (await consumer.StateOfAsync(account)).ShouldBe((5L, 50.00m));
    }

    private Task Publish(Guid account, long version, decimal balance) =>
        RawEventPublisher.PublishAsync(broker, Guid.NewGuid(), RawEventPublisher.EventJson(account, version, balance));

    private static string UniqueQueue() => $"reference-consumer.{Guid.NewGuid():N}.ledger.entry-registered";
}
