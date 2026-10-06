using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Consumers;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class EventDeduplicationTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task AnEventRepublishedByTheWorker_HasItsEffectAppliedOnlyOnce()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var consumer = await ReferenceConsumer.StartAsync(broker, scenario.Database, UniqueQueue());
        var account = Guid.NewGuid();

        var ids = await scenario.Seeder.InsertEventsAsync(
        [
            new OutboxEventSeed(account, 1, 10.00m, TimeSpan.FromSeconds(1)),
            new OutboxEventSeed(account, 2, 20.00m, TimeSpan.FromSeconds(2)),
            new OutboxEventSeed(account, 3, 30.00m, TimeSpan.FromSeconds(3))
        ]);

        scenario.StartWorker();

        await ConditionWait.UntilAsync(
            () => consumer.Applied + consumer.Ignored == 3,
            Patience,
            "the consumer to process the first delivery");
        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 0,
            Patience,
            "the worker to mark every message");

        await scenario.Seeder.MarkPendingAgainAsync(ids);

        await ConditionWait.UntilAsync(
            () => consumer.Duplicates == 3,
            Patience,
            "the consumer to recognise the three redeliveries");

        (await consumer.ProcessedCountAsync()).ShouldBe(3);
        (await consumer.StateOfAsync(account)).ShouldBe((3L, 30.00m));
        consumer.Rejected.ShouldBe(0);
    }

    [DockerFact]
    public async Task TheSameMessageDeliveredTwiceByTheBroker_IsAppliedOnce()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var consumer = await ReferenceConsumer.StartAsync(broker, scenario.Database, UniqueQueue());
        var account = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var body = RawEventPublisher.EventJson(account, 1, 10.00m);

        await RawEventPublisher.PublishAsync(broker, messageId, body);
        await RawEventPublisher.PublishAsync(broker, messageId, body);

        await ConditionWait.UntilAsync(
            () => consumer.Applied == 1 && consumer.Duplicates == 1,
            Patience,
            "the consumer to apply once and recognise the repetition");

        (await consumer.ProcessedCountAsync()).ShouldBe(1);
    }

    private static string UniqueQueue() => $"reference-consumer.{Guid.NewGuid():N}.ledger.entry-registered";
}
