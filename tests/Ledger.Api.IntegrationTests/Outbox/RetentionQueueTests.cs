using Ledger.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client.Exceptions;
using Serilog.Core;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class RetentionQueueTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private static Dictionary<string, object?> ExpectedRetentionArguments(string queue) =>
        new()
        {
            ["x-queue-type"] = "quorum",
            ["x-message-ttl"] = 86_400_000L,
            ["x-max-length"] = 1_000_000,
            ["x-max-length-bytes"] = 1_073_741_824L,
            ["x-overflow"] = "drop-head",
            ["x-dead-letter-exchange"] = queue + ".dlx",
            ["x-dead-letter-routing-key"] = "dead"
        };

    [DockerFact]
    public async Task Worker_WithTwentyPendingEvents_LeavesExactlyTwentyInTheRetentionQueueWithoutDuplicates()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var ids = await scenario.Seeder.InsertPendingAsync(20);

        scenario.StartWorker();

        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 0,
            Patience,
            "every event marked as published");
        await Task.Delay(TimeSpan.FromSeconds(1));

        var rows = await scenario.Seeder.ReadRowsAsync();
        var count = await scenario.Retention.CountAsync();
        var messages = await scenario.Retention.DrainAsync();

        count.ShouldBe(20u);
        messages.Count.ShouldBe(20);
        messages.Select(message => message.MessageId).Distinct().Count().ShouldBe(20);
        messages.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ToHashSet().ShouldBe(ids.ToHashSet());
        messages.ShouldAllBe(message => message.RoutingKey == RabbitMqFixture.RoutingKey);
        rows.ShouldAllBe(row => row.PublishedAt != null && row.LockedUntil == null && row.Attempts == 1);
    }

    [DockerFact]
    public async Task Worker_WithAConsumerQueueAndTheRetentionQueue_DeliversEachEventToBoth()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        var ids = await scenario.Seeder.InsertPendingAsync(20);

        scenario.StartWorker();

        var consumed = await scenario.RequiredProbe.ReceiveAsync(ids.Count, Patience);

        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 0,
            Patience,
            "every event marked as published");

        var retained = await scenario.Retention.DrainAsync();

        consumed.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ToHashSet().ShouldBe(ids.ToHashSet());
        retained.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ToHashSet().ShouldBe(ids.ToHashSet());
        retained.Count.ShouldBe(20);
    }

    [DockerFact]
    public async Task Worker_OnConnect_DeclaresTheRetentionTopologyWithTheDocumentedArguments()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var retention = scenario.Retention;

        scenario.StartWorker();

        await ConditionWait.UntilAsync(
            async () => await retention.ExistsAsync() && await retention.ExistsAsync(retention.DeadLetterQueue),
            Patience,
            "the retention queue and its dead letter queue to be declared");

        await retention.DeclareWithAsync(retention.Queue, ExpectedRetentionArguments(retention.Queue));

        var changed = ExpectedRetentionArguments(retention.Queue);

        changed["x-message-ttl"] = 3_600_000L;

        var mismatch = await Should.ThrowAsync<OperationInterruptedException>(
            () => retention.DeclareWithAsync(retention.Queue, changed));

        mismatch.ShutdownReason.ShouldNotBeNull().ReplyCode.ShouldBe((ushort)406);
    }

    [DockerFact]
    public async Task Worker_OnConnect_DeclaresTheDeadLetterQueueWithItsOwnLimits()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var retention = scenario.Retention;

        scenario.StartWorker();

        await ConditionWait.UntilAsync(
            async () => await retention.ExistsAsync(retention.DeadLetterQueue),
            Patience,
            "the dead letter queue to be declared");

        await retention.DeclareWithAsync(
            retention.DeadLetterQueue,
            new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-message-ttl"] = 604_800_000L,
                ["x-max-length"] = 100_000,
                ["x-overflow"] = "drop-head"
            });
    }

    [DockerFact]
    public async Task RetentionQueue_BoundWithTheEventTypeKey_DoesNotReceiveOtherKeys()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var retention = scenario.Retention;
        var sink = new WorkerLogSink();

        scenario.StartWorker(
            new Dictionary<string, string?> { ["Serilog:MinimumLevel:Default"] = "Information" },
            services => services.AddSingleton<ILogEventSink>(sink));

        await ConditionWait.UntilAsync(
            () => sink.WithId(3006).Count == 1,
            Patience,
            "the worker to connect after binding the retention queue");

        await RawEventPublisher.PublishAsync(broker, Guid.NewGuid(), "{}");
        await RawEventPublisher.PublishWithKeyAsync(broker, "SomethingElse", Guid.NewGuid(), "{}");

        await ConditionWait.UntilAsync(
            async () => await retention.CountAsync() >= 1,
            Patience,
            "the event with the bound key to be retained");
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        (await retention.CountAsync()).ShouldBe(1u);
    }

    [DockerFact]
    public async Task Worker_WithTheRetentionQueueDisabled_DeclaresNoQueue()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var sink = new WorkerLogSink();

        scenario.StartWorker(
            new Dictionary<string, string?>
            {
                ["RabbitMq:Retention:Enabled"] = "false",
                ["Serilog:MinimumLevel:Default"] = "Information"
            },
            services => services.AddSingleton<ILogEventSink>(sink));

        await ConditionWait.UntilAsync(
            () => sink.WithId(3006).Count == 1,
            Patience,
            "the worker to connect");

        (await scenario.Retention.ExistsAsync()).ShouldBeFalse();
        (await scenario.Retention.ExistsAsync(scenario.Retention.DeadLetterQueue)).ShouldBeFalse();
    }

    [DockerFact]
    public async Task Worker_WhenTheQueueExistsWithOtherArguments_KeepsItBindsItAndKeepsPublishing()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var sink = new WorkerLogSink();

        await scenario.Retention.DeclareAndBindWithAsync(new Dictionary<string, object?> { ["x-max-length"] = 5000 });

        var ids = await scenario.Seeder.InsertPendingAsync(5);

        scenario.StartWorker(configureServices: services => services.AddSingleton<ILogEventSink>(sink));

        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 0,
            Patience,
            "every event marked as published");

        var kept = sink.WithId(3010).ShouldHaveSingleItem();
        var retained = await scenario.Retention.DrainAsync();

        kept.Properties["Queue"].ShouldBe(scenario.RetentionQueue);
        retained.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ToHashSet().ShouldBe(ids.ToHashSet());
    }
}
