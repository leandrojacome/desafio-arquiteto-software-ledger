using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Consumers;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Resilience")]
public sealed class ConsumerCrashBetweenEffectAndAckTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task AConsumerThatDiesAfterCommittingTheEffect_GetsTheRedeliveryAndRecognisesItAsADuplicate()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);

        var queue = $"reference-consumer.{Guid.NewGuid():N}.ledger.entry-registered";
        var account = Guid.NewGuid();

        await using var first = await ReferenceConsumer.StartAsync(
            broker,
            scenario.Database,
            queue,
            crashBeforeAck: _ => true);

        await scenario.Seeder.InsertEventsAsync([new OutboxEventSeed(account, 1, 10.00m, TimeSpan.FromSeconds(1))]);

        scenario.StartWorker();

        await ConditionWait.UntilAsync(() => first.Crashed, Patience, "the first consumer to crash");

        (await first.ProcessedCountAsync()).ShouldBe(1);
        (await first.StateOfAsync(account)).ShouldBe((1L, 10.00m));

        await using var second = await ReferenceConsumer.StartAsync(broker, scenario.Database, queue);

        await ConditionWait.UntilAsync(
            () => second.Duplicates == 1,
            Patience,
            "the redelivery to be recognised as a duplicate");

        await ConditionWait.UntilAsync(
            async () => await second.ReadyCountAsync() == 0,
            Patience,
            "the queue to be empty");

        (await second.ProcessedCountAsync()).ShouldBe(1);
        (await second.StateOfAsync(account)).ShouldBe((1L, 10.00m));
        second.Applied.ShouldBe(0);
    }
}
