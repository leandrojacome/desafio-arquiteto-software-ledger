using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Consumers;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class UnknownFieldToleranceTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task AnEventWithAFieldTheConsumerDoesNotKnow_IsAcceptedAndTheFieldIsIgnored()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var consumer = await ReferenceConsumer.StartAsync(
            broker,
            scenario.Database,
            $"reference-consumer.{Guid.NewGuid():N}.ledger.entry-registered");
        var account = Guid.NewGuid();

        await scenario.Seeder.InsertEventsAsync(
            [new OutboxEventSeed(account, 1, 10.00m, TimeSpan.FromSeconds(1), ExtraField: "futureField")]);

        scenario.StartWorker();

        await ConditionWait.UntilAsync(() => consumer.Applied == 1, Patience, "the event to be applied");

        consumer.Rejected.ShouldBe(0);
        (await consumer.StateOfAsync(account)).ShouldBe((1L, 10.00m));
    }
}
