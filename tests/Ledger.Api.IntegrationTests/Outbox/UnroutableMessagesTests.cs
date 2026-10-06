using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Resilience")]
public sealed class UnroutableMessagesTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    [DockerFact]
    public async Task WhenTheRetentionQueueIsDeleted_TheMessagesAreReportedUnroutableStayPendingAndDrainOnceTheQueueIsBack()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        scenario.UsePrivateExchange();
        var sink = new WorkerLogSink();
        using var failures = new TaggedCounter("Ledger", "outbox.publish.failures");
        var worker = scenario.StartWorker(
            new Dictionary<string, string?>
            {
                ["RabbitMq:ReconnectMinSeconds"] = "12",
                ["RabbitMq:ReconnectMaxSeconds"] = "12",
                ["Serilog:MinimumLevel:Default"] = "Information"
            },
            services => services.AddSingleton<ILogEventSink>(sink));
        var publisher = worker.Services.GetRequiredService<IEventPublisher>();

        await ConditionWait.UntilAsync(
            async () => publisher.IsConnected && await scenario.Retention.ExistsAsync(),
            Patience,
            "the worker to connect and declare the retention queue");

        await scenario.Retention.DeleteQueueAsync();
        (await scenario.Retention.ExistsAsync()).ShouldBeFalse();

        var ids = await scenario.Seeder.InsertPendingAsync(20);

        await ConditionWait.UntilAsync(
            () => failures.TotalWhere("reason", "unroutable") >= 20,
            Patience,
            "every message to be reported unroutable");
        await ConditionWait.UntilAsync(
            async () => (await scenario.Seeder.ReadRowsAsync()).All(row => row.Attempts == 0 && row.LockedUntil is null),
            TimeSpan.FromSeconds(8),
            "the unroutable messages to be released without a charged attempt");

        var rowsWhileUnroutable = await scenario.Seeder.ReadRowsAsync();
        var unroutable = sink.WithId(3009).ShouldHaveSingleItem();

        rowsWhileUnroutable.ShouldAllBe(row => row.PublishedAt == null);
        publisher.IsConnected.ShouldBeFalse();
        publisher.Circuit.ShouldBe(BrokerCircuitState.Closed);
        unroutable.Level.ShouldBe(LogEventLevel.Warning);
        unroutable.Properties["Unroutable"].ShouldBe(20);
        sink.WithId(3002).ShouldBeEmpty();

        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 0,
            Patience,
            "the outbox to drain once the worker declared the queue again");

        var retained = await scenario.Retention.DrainAsync();
        var rowsAfter = await scenario.Seeder.ReadRowsAsync();

        retained.Count.ShouldBe(20);
        retained.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ToHashSet().ShouldBe(ids.ToHashSet());
        rowsAfter.ShouldAllBe(row => row.PublishedAt != null && row.Attempts == 1);
        failures.TotalWhere("reason", "unroutable").ShouldBe(20);
        sink.WithId(3011).ShouldHaveSingleItem().Level.ShouldBe(LogEventLevel.Information);
    }

    [DockerFact]
    public async Task WithNoQueueBoundAnywhere_TheMessagesWaitInTheOutboxAndDrainWhenAConsumerDeclaresItsQueue()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var exchange = scenario.UsePrivateExchange();
        var sink = new WorkerLogSink();
        using var failures = new TaggedCounter("Ledger", "outbox.publish.failures");
        var ids = await scenario.Seeder.InsertPendingAsync(10);
        var worker = scenario.StartWorker(
            new Dictionary<string, string?> { ["RabbitMq:Retention:Enabled"] = "false" },
            services => services.AddSingleton<ILogEventSink>(sink));
        var publisher = worker.Services.GetRequiredService<IEventPublisher>();
        using var client = worker.CreateClient();

        await ConditionWait.UntilAsync(
            () => failures.TotalWhere("reason", "unroutable") >= 10,
            Patience,
            "the broker to return every message");

        var rows = await scenario.Seeder.ReadRowsAsync();

        rows.ShouldAllBe(row => row.PublishedAt == null);
        publisher.Circuit.ShouldBe(BrokerCircuitState.Closed);
        (await ReadinessAsync(client)).ShouldBe(HttpStatusCode.OK);
        (await ReadinessAsync(client, "/health/live")).ShouldBe(HttpStatusCode.OK);

        await using var consumer = await QueueProbe.CreateAsync(broker, exchange: exchange);

        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 0,
            Patience,
            "the outbox to drain once a queue is bound");

        var received = await consumer.DrainAsync();

        received.Count.ShouldBe(10);
        received.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ToHashSet().ShouldBe(ids.ToHashSet());
    }

    [DockerFact]
    public async Task UnroutableMessages_NeverAccumulateAttemptsNorRaiseTheStuckMessageAlarm()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        scenario.UsePrivateExchange();
        var sink = new WorkerLogSink();
        using var failures = new TaggedCounter("Ledger", "outbox.publish.failures");

        await scenario.Seeder.InsertPendingAsync(3);

        scenario.StartWorker(
            new Dictionary<string, string?>
            {
                ["RabbitMq:Retention:Enabled"] = "false",
                ["RabbitMq:ReconnectMinSeconds"] = "1",
                ["RabbitMq:ReconnectMaxSeconds"] = "1",
                ["Outbox:FailedAttempts"] = "2"
            },
            services => services.AddSingleton<ILogEventSink>(sink));

        await ConditionWait.UntilAsync(
            () => failures.TotalWhere("reason", "unroutable") >= 12,
            Patience,
            "the same three messages to come back four times");

        var rows = await scenario.Seeder.ReadRowsAsync();

        rows.ShouldAllBe(row => row.Attempts <= 1 && row.PublishedAt == null);
        sink.WithId(3005).ShouldBeEmpty();
    }

    private static async Task<HttpStatusCode> ReadinessAsync(HttpClient client, string route = "/health/ready")
    {
        using var response = await client.GetAsync(route, CancellationToken.None);

        return response.StatusCode;
    }
}
