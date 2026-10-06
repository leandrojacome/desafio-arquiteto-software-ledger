using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
[Trait("Speed", "Slow")]
public sealed class PartialConfirmationTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task WhenFiveOfTwentyMessagesAreRefused_OnlyTheConfirmedOnesAreMarkedAndTheRestStayPending()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        var ids = await scenario.Seeder.InsertPendingAsync(20);
        var refused = ids.Take(5).ToHashSet();
        var faults = new Holder<FaultInjectingEventPublisher>();

        scenario.StartWorker(
            new Dictionary<string, string?> { ["Outbox:LeaseSeconds"] = "300" },
            services => services.Decorate<IEventPublisher>((inner, _) =>
            {
                var publisher = new FaultInjectingEventPublisher(inner)
                {
                    AlwaysFail = envelope => refused.Contains(envelope.Id)
                };

                faults.Value = publisher;

                return publisher;
            }));

        var received = await scenario.RequiredProbe.ReceiveAsync(15, Patience);

        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 5,
            Patience,
            "the fifteen confirmed messages to be marked");

        var rows = await scenario.Seeder.ReadRowsAsync();
        var pending = rows.Where(row => row.PublishedAt is null).ToList();

        received.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ShouldBe(
            ids.Skip(5),
            ignoreOrder: true);
        pending.Select(row => row.Id).ToHashSet().ShouldBe(refused);
        pending.ShouldAllBe(row => row.Attempts == 1 && row.LockedUntil != null);
        rows.Where(row => row.PublishedAt is not null).ShouldAllBe(row => row.LockedUntil == null);
        faults.Value.ShouldNotBeNull();
    }

    [DockerFact]
    public async Task ARefusedMessageIsClaimedAgainWhenItsLeaseExpires_WhileTheOthersAreNotRepublished()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        var ids = await scenario.Seeder.InsertPendingAsync(6);
        var refused = ids[0];
        var faults = new Holder<FaultInjectingEventPublisher>();

        scenario.StartWorker(
            configureServices: services => services.Decorate<IEventPublisher>((inner, _) =>
            {
                var publisher = new FaultInjectingEventPublisher(inner)
                {
                    AlwaysFail = envelope => envelope.Id == refused
                };

                faults.Value = publisher;

                return publisher;
            }));

        await ConditionWait.UntilAsync(
            async () => (await scenario.Seeder.ReadRowsAsync()).First(row => row.Id == refused).Attempts >= 2,
            Patience,
            "the refused message to be claimed a second time");

        var received = await scenario.RequiredProbe.ReceiveAsync(5, Patience);
        var rows = await scenario.Seeder.ReadRowsAsync();

        received.Select(message => message.MessageId).Distinct().Count().ShouldBe(5);
        rows.Where(row => row.Id != refused).ShouldAllBe(row => row.Attempts == 1 && row.PublishedAt != null);
        rows.Single(row => row.Id == refused).PublishedAt.ShouldBeNull();
    }

    [DockerFact]
    public async Task AMessageTheBrokerNeverAccepts_IsRetriedEveryLeaseCountedAsFailedAndNeverDeleted()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        var ids = await scenario.Seeder.InsertPendingAsync(4);
        var poison = ids[0];
        var sink = new WorkerLogSink();

        var worker = scenario.StartWorker(
            new Dictionary<string, string?> { ["Outbox:FailedAttempts"] = "3" },
            services =>
            {
                services.AddSingleton<ILogEventSink>(sink);
                services.Decorate<IEventPublisher>((inner, _) =>
                    new FaultInjectingEventPublisher(inner) { AlwaysFail = envelope => envelope.Id == poison });
            });

        var stats = worker.Services.GetRequiredService<OutboxStatsHolder>();

        await ConditionWait.UntilAsync(
            () => stats.Snapshot is { Failed: >= 1 },
            TimeSpan.FromSeconds(90),
            "the poison message to reach the failed threshold");

        var rows = await scenario.Seeder.ReadRowsAsync();
        var received = await scenario.RequiredProbe.ReceiveAsync(3, Patience);

        rows.Single(row => row.Id == poison).Attempts.ShouldBeGreaterThanOrEqualTo(3);
        rows.Single(row => row.Id == poison).PublishedAt.ShouldBeNull();
        rows.Where(row => row.Id != poison).ShouldAllBe(row => row.Attempts == 1 && row.PublishedAt != null);
        received.Count.ShouldBe(3);
        stats.Snapshot.ShouldNotBeNull().Failed.ShouldBe(1);

        var stuck = sink.WithId(3005);

        stuck.ShouldNotBeEmpty();
        stuck.ShouldAllBe(captured => captured.Level == LogEventLevel.Warning
                                      && (Guid)captured.Properties["MessageId"] == poison
                                      && (int)captured.Properties["Attempts"] >= 3);
    }

    private sealed class Holder<T>
        where T : class
    {
        public T? Value { get; set; }
    }
}
