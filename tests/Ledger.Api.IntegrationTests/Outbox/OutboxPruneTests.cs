using Ledger.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxPruneTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task Worker_RemovesOnlyMessagesPublishedLongerAgoThanTheRetention_InBatchesUntilEmpty()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        using var pruned = new MeterCounter("Ledger", "outbox.pruned");
        var sink = new WorkerLogSink();
        var old = await scenario.Seeder.InsertPendingAsync(250);
        var recent = await scenario.Seeder.InsertPendingAsync(3);
        var pending = await scenario.Seeder.InsertPendingAsync(4);

        await scenario.Seeder.MarkPublishedAgoAsync(old, TimeSpan.FromDays(8));
        await scenario.Seeder.MarkPublishedAgoAsync(recent, TimeSpan.FromDays(6));

        scenario.StartWorker(
            new Dictionary<string, string?>
            {
                ["Outbox:PruneBatchSize"] = "100",
                ["RabbitMq:Port"] = "1",
                ["Serilog:MinimumLevel:Default"] = "Information"
            },
            services => services.AddSingleton<ILogEventSink>(sink));

        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountRowsAsync() == recent.Count + pending.Count,
            Patience,
            "the old published messages to be pruned");
        await ConditionWait.UntilAsync(() => sink.WithId(3004).Count > 0, Patience, "the prune to be logged");

        var remaining = (await scenario.Seeder.ReadRowsAsync()).Select(row => row.Id).ToHashSet();

        remaining.ShouldBe(recent.Concat(pending).ToHashSet());
        pruned.Total.ShouldBe(250);

        var logged = sink.WithId(3004).First(captured => captured.Level == LogEventLevel.Information);

        logged.Properties["Removed"].ShouldBe(250);
    }

    [DockerFact]
    public async Task Worker_NeverPrunesAMessageThatIsStillPending_NoMatterHowOldItIs()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var sink = new WorkerLogSink();
        var ids = await scenario.Seeder.InsertPendingAsync(5);

        scenario.StartWorker(
            new Dictionary<string, string?>
            {
                ["RabbitMq:Port"] = "1",
                ["Serilog:MinimumLevel:Default"] = "Debug"
            },
            services => services.AddSingleton<ILogEventSink>(sink));

        await ConditionWait.UntilAsync(
            () => sink.WithId(3004).Count > 0,
            Patience,
            "the prune cycle to run and report that it removed nothing");

        sink.WithId(3004).ShouldAllBe(captured => (int)captured.Properties["Removed"] == 0);

        (await scenario.Seeder.CountRowsAsync()).ShouldBe(ids.Count);
        (await scenario.Seeder.CountPendingAsync()).ShouldBe(ids.Count);
    }
}
