using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Consumers;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class DeadLetterTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task UnreadableAndUnsupportedMessages_GoToTheDeadLetterQueueWhileTheOthersKeepBeingConsumed()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var consumer = await ReferenceConsumer.StartAsync(
            broker,
            scenario.Database,
            $"reference-consumer.{Guid.NewGuid():N}.ledger.entry-registered");
        var account = Guid.NewGuid();

        await RawEventPublisher.PublishAsync(broker, Guid.NewGuid(), "this is not json");
        await RawEventPublisher.PublishAsync(broker, Guid.NewGuid(), RawEventPublisher.EventJson(account, 1, 10m, schemaVersion: 2));
        await RawEventPublisher.PublishAsync(broker, Guid.NewGuid(), RawEventPublisher.EventJson(account, 1, 10m));
        await RawEventPublisher.PublishAsync(broker, Guid.NewGuid(), RawEventPublisher.EventJson(account, 2, 20m));

        await ConditionWait.UntilAsync(
            () => consumer.Rejected == 2 && consumer.Applied == 2,
            Patience,
            "the consumer to reject two messages and apply two");
        await ConditionWait.UntilAsync(
            async () => await consumer.DeadLetterCountAsync() == 2,
            Patience,
            "the dead letter queue to receive the two rejected messages");

        (await consumer.StateOfAsync(account)).ShouldBe((2L, 20.00m));
        (await consumer.ReadyCountAsync()).ShouldBe(0u);
    }

    [DockerFact]
    public async Task AMessageWithoutAValidMessageId_IsAlsoRejectedWithoutRequeue()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var consumer = await ReferenceConsumer.StartAsync(
            broker,
            scenario.Database,
            $"reference-consumer.{Guid.NewGuid():N}.ledger.entry-registered");

        await using var connection = await broker.CreateConnectionAsync(CancellationToken.None);
        await using var channel = await connection.CreateChannelAsync();

        await channel.BasicPublishAsync(
            RabbitMqFixture.Exchange,
            RabbitMqFixture.RoutingKey,
            mandatory: false,
            new RabbitMQ.Client.BasicProperties { MessageId = "not-a-guid" },
            System.Text.Encoding.UTF8.GetBytes(RawEventPublisher.EventJson(Guid.NewGuid(), 1, 1m)));

        await ConditionWait.UntilAsync(() => consumer.Rejected == 1, Patience, "the message to be rejected");

        (await consumer.ProcessedCountAsync()).ShouldBe(0);
    }
}
