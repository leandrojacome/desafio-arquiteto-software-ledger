using System.Net;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Resilience")]
[Trait("Speed", "Slow")]
public sealed class WorkerDatabaseOutageTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(120);

    [DockerFact]
    public async Task WhenTheDatabaseGoesAway_TheLoopsFailAndBackOffTheWorkerStaysAliveAndRecoversWithoutARestart()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: true);
        await using var proxy = TcpProxy.Start(
            postgres.Configuration["Postgres:Host"] ?? "127.0.0.1",
            int.Parse(postgres.Configuration["Postgres:Port"] ?? "5432", System.Globalization.CultureInfo.InvariantCulture));

        var sink = new WorkerLogSink();
        var firstBatch = await scenario.Seeder.InsertPendingAsync(20);

        var worker = scenario.StartWorker(
            new Dictionary<string, string?>
            {
                ["Postgres:Host"] = "127.0.0.1",
                ["Postgres:Port"] = proxy.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Postgres:Sources:Worker:ConnectionTimeoutSeconds"] = "2"
            },
            services => services.AddSingleton<ILogEventSink>(sink));
        using var client = worker.CreateClient();

        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 0,
            Patience,
            "the first batch to be published through the proxy");

        proxy.Pause();

        var secondBatch = await scenario.Seeder.InsertPendingAsync(20);

        await ConditionWait.UntilAsync(
            () => sink.WithId(3008).Count(captured => (string)captured.Properties["Loop"] == "outbox") >= 2,
            Patience,
            "the outbox loop to report two failures");

        var delays = sink.WithId(3008)
            .Where(captured => (string)captured.Properties["Loop"] == "outbox")
            .Select(captured => (double)captured.Properties["NextDelaySeconds"])
            .ToList();

        delays[^1].ShouldBeGreaterThanOrEqualTo(delays[0] * 0.8);
        sink.WithId(3008).ShouldAllBe(captured => captured.Level == Serilog.Events.LogEventLevel.Warning);

        using var ready = await client.GetAsync("/health/ready", CancellationToken.None);

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        ready.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(5));
        (await StatusOfAsync(client, "/health/live")).ShouldBe((HttpStatusCode.OK, "Healthy"));
        (await scenario.Seeder.CountPendingAsync()).ShouldBe(secondBatch.Count);

        proxy.Resume();

        await ConditionWait.UntilAsync(
            async () => await scenario.Seeder.CountPendingAsync() == 0,
            Patience,
            "the backlog to drain after the database came back");

        var received = await scenario.RequiredProbe.DrainAsync();

        received.Select(message => Guid.Parse(message.MessageId ?? string.Empty)).ToHashSet()
            .ShouldBe(firstBatch.Concat(secondBatch).ToHashSet());

        await ConditionWait.UntilAsync(
            async () => (await StatusOfAsync(client, "/health/ready")).Item2 == "Healthy",
            Patience,
            "readiness to come back");

        worker.Services.GetRequiredService<Ledger.Application.Abstractions.IWorkerHeartbeat>().ShouldNotBeNull();
    }

    private static async Task<(HttpStatusCode, string?)> StatusOfAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route, CancellationToken.None);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.GetProperty("status").GetString());
    }
}
