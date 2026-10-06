using System.Net;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Resilience")]
[Trait("Speed", "Slow")]
public sealed class BrokerOutageTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(120);

    [DockerFact]
    public async Task WithTheBrokerDownFromTheStart_TheOutboxAccumulatesTheWorkerStaysUpAndDrainsWhenTheBrokerReturns()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);

        try
        {
            await broker.StopAsync(CancellationToken.None);

            var ids = await scenario.Seeder.InsertPendingAsync(100);
            var sink = new WorkerLogSink();
            var worker = scenario.StartWorker(
                configureServices: services => services.AddSingleton<ILogEventSink>(sink));
            using var client = worker.CreateClient();

            await ConditionWait.UntilAsync(
                () => sink.WithId(3007).Count >= 2,
                Patience,
                "the worker to try the broker twice and fail");

            var rows = await scenario.Seeder.ReadRowsAsync();

            rows.Count(row => row.PublishedAt is null).ShouldBe(100);
            rows.ShouldAllBe(row => row.Attempts == 0);
            (await StatusOfAsync(client, "/health/live")).ShouldBe((HttpStatusCode.OK, "Healthy"));
            (await StatusOfAsync(client, "/health/ready")).ShouldBe((HttpStatusCode.OK, "Degraded"));

            await broker.StartAsync(CancellationToken.None);
            await scenario.RequiredProbe.ReconnectAsync();

            await ConditionWait.UntilAsync(
                async () => await scenario.Seeder.CountPendingAsync() == 0,
                Patience,
                "the backlog to drain after the broker returned");

            var received = await scenario.RequiredProbe.DrainAsync();

            received.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).Distinct().Count()
                .ShouldBe(ids.Count);
            (await StatusOfAsync(client, "/health/live")).Item1.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await EnsureBrokerRunningAsync();
        }
    }

    [DockerFact]
    public async Task WhenTheBrokerStopsInTheMiddleOfTheBacklog_NothingIsLostAndTheRestDrainsAfterItReturns()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        var ids = await scenario.Seeder.InsertPendingAsync(2000);

        try
        {
            var worker = scenario.StartWorker(new Dictionary<string, string?> { ["Outbox:BatchSize"] = "20" });
            using var client = worker.CreateClient();
            var publisher = worker.Services.GetRequiredService<IEventPublisher>();

            await ConditionWait.UntilAsync(
                async () =>
                {
                    var pending = await scenario.Seeder.CountPendingAsync();

                    return pending is > 0 and < 1999;
                },
                Patience,
                "the backlog to be partly published");

            await broker.StopAsync(CancellationToken.None);

            await ConditionWait.UntilAsync(
                () => !publisher.IsConnected,
                Patience,
                "the worker to notice that the broker stopped");

            (await StatusOfAsync(client, "/health/live")).Item1.ShouldBe(HttpStatusCode.OK);

            var remaining = await scenario.Seeder.CountPendingAsync();

            await broker.StartAsync(CancellationToken.None);
            await scenario.RequiredProbe.ReconnectAsync();

            await ConditionWait.UntilAsync(
                async () => await scenario.Seeder.CountPendingAsync() == 0,
                Patience,
                "the backlog to drain after the broker returned");

            var received = await scenario.RequiredProbe.DrainAsync();

            remaining.ShouldBeGreaterThan(0);
            remaining.ShouldBeLessThan(ids.Count);
            received.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ToHashSet().ShouldBe(ids.ToHashSet());
        }
        finally
        {
            await EnsureBrokerRunningAsync();
        }
    }

    [DockerFact]
    public async Task WithTheBrokerPaused_PublicationsTimeOutTheCircuitOpensAndNoMoreMessagesAreClaimed()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);

        try
        {
            var worker = scenario.StartWorker(new Dictionary<string, string?>
            {
                ["Resilience:BrokerCircuitBreaker:BreakSeconds"] = "20",
                ["Outbox:ConfirmTimeoutSeconds"] = "2",
                ["Outbox:LeaseSeconds"] = "60"
            });
            var publisher = worker.Services.GetRequiredService<IEventPublisher>();

            await ConditionWait.UntilAsync(() => publisher.IsConnected, Patience, "the worker to connect to the broker");

            await broker.PauseAsync(CancellationToken.None);

            var ids = await scenario.Seeder.InsertPendingAsync(60);

            await ConditionWait.UntilAsync(
                () => publisher.Circuit == BrokerCircuitState.Open,
                Patience,
                "the broker circuit to open");

            await ConditionWait.UntilAsync(
                async () => (await scenario.Seeder.ReadRowsAsync()).All(row => row.Attempts == 0 && row.LockedUntil is null),
                Patience,
                "the messages that waited for the paused broker to be released without a charged attempt");

            publisher.ClaimBudget(200).ShouldBe(0);
            (await scenario.Seeder.ReadRowsAsync()).ShouldAllBe(row => row.PublishedAt == null);

            await broker.UnpauseAsync(CancellationToken.None);

            await ConditionWait.UntilAsync(
                async () => await scenario.Seeder.CountPendingAsync() == 0,
                Patience,
                "the backlog to drain once the circuit closed again");

            await ConditionWait.UntilAsync(
                () => publisher.Circuit == BrokerCircuitState.Closed,
                Patience,
                "the circuit to close");

            var received = await scenario.RequiredProbe.DrainAsync();

            received.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ToHashSet().ShouldBe(ids.ToHashSet());
        }
        finally
        {
            await EnsureBrokerRunningAsync();
        }
    }

    private static async Task<(HttpStatusCode, string?)> StatusOfAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route, CancellationToken.None);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.GetProperty("status").GetString());
    }

    private async Task EnsureBrokerRunningAsync()
    {
        try
        {
            await broker.UnpauseAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            await Task.CompletedTask;
        }

        await broker.StartAsync(CancellationToken.None);
    }
}
