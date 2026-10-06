using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Resilience")]
[Trait("Speed", "Slow")]
public sealed class PublisherKilledBetweenPublishAndMarkTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task WhenTheWorkerDiesAfterPublishingAndBeforeMarking_ASecondWorkerRepublishesWithTheSameMessageId()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        var ids = await scenario.Seeder.InsertPendingAsync(10);

        var doomed = scenario.StartWorker(
            configureServices: services => services.Decorate<IOutboxQueue>((inner, _) =>
                new FaultInjectingOutboxQueue(inner) { FailMarking = true }));

        var firstDelivery = await scenario.RequiredProbe.ReceiveAsync(10, Patience);

        await scenario.StopWorkerAsync(doomed);

        (await scenario.Seeder.CountPendingAsync()).ShouldBe(10);

        scenario.StartWorker();

        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 0,
            Patience,
            "the survivor to mark every message");

        var laterDelivery = await scenario.RequiredProbe.DrainAsync();
        var delivered = firstDelivery.Concat(laterDelivery).ToList();

        delivered.Count.ShouldBeGreaterThanOrEqualTo(20);
        delivered.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).Distinct().Count().ShouldBe(10);
        delivered.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ToHashSet().ShouldBe(ids.ToHashSet());
        (await scenario.Seeder.ReadRowsAsync()).ShouldAllBe(row => row.PublishedAt != null && row.Attempts >= 2);
    }
}
