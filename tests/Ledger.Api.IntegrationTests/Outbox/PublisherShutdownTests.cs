using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Resilience")]
public sealed class PublisherShutdownTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task OnShutdown_TheBatchInFlightIsPublishedAndMarked_AndNoOtherBatchIsClaimed()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        await scenario.Seeder.InsertPendingAsync(300);
        var holder = new Holder();

        var worker = scenario.StartWorker(
            new Dictionary<string, string?>
            {
                ["Outbox:BatchSize"] = "100",
                ["Outbox:ConfirmTimeoutSeconds"] = "4",
                ["Outbox:LeaseSeconds"] = "60"
            },
            services => services.Decorate<IEventPublisher>((inner, _) =>
            {
                var publisher = new FaultInjectingEventPublisher(inner) { Delay = TimeSpan.FromSeconds(2) };

                holder.Value = publisher;

                return publisher;
            }));

        await ConditionWait.UntilAsync(() => holder.Value is not null, Patience, "the publisher to be created");

        var faults = holder.Value ?? throw new InvalidOperationException("The publisher was not created.");

        await faults.FirstPublicationStarted.WaitAsync(Patience);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await scenario.StopWorkerAsync(worker);

        stopwatch.Stop();

        var rows = await scenario.Seeder.ReadRowsAsync();
        var published = rows.Where(row => row.PublishedAt is not null).ToList();
        var untouched = rows.Where(row => row.PublishedAt is null).ToList();

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        published.Count.ShouldBe(100);
        published.ShouldAllBe(row => row.Attempts == 1 && row.LockedUntil == null);
        untouched.Count.ShouldBe(200);
        untouched.ShouldAllBe(row => row.Attempts == 0 && row.LockedUntil == null);
        (await scenario.RequiredProbe.ReceiveAsync(100, Patience)).Count.ShouldBe(100);
    }

    private sealed class Holder
    {
        public FaultInjectingEventPublisher? Value { get; set; }
    }
}
