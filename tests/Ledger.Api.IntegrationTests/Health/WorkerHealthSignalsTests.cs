using System.Net;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ledger.Api.IntegrationTests.Health;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class WorkerHealthSignalsTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task Ready_WithTheDatabaseAndTheBrokerUp_IsHealthyWithOnlyTheAggregateStateInTheBody()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var worker = scenario.StartWorker();
        using var client = worker.CreateClient();

        await ConditionWait.UntilAsync(
            async () => (await StatusAsync(client, "/health/ready")).State == "Healthy",
            Patience,
            "readiness to become healthy");

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        body.ShouldBe("{\"status\":\"Healthy\"}");
    }

    [DockerFact]
    public async Task BothRoutes_AnswerHeadWithoutABody_AndRefuseOtherMethodsWithAnAllowHeader()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var worker = scenario.StartWorker();
        using var client = worker.CreateClient();

        foreach (var route in new[] { "/health/live", "/health/ready" })
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, route);
            using var postBody = new StringContent(string.Empty);
            using var head = await client.SendAsync(headRequest, CancellationToken.None);
            using var post = await client.PostAsync(route, postBody, CancellationToken.None);

            head.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
            (await head.Content.ReadAsStringAsync(CancellationToken.None)).ShouldBeEmpty();
            post.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
            post.Content.Headers.Allow.ShouldBe(["GET", "HEAD"], ignoreOrder: true);
        }
    }

    [DockerFact]
    public async Task Live_IsHealthyWhileTheLoopsTurn_AndNeverTouchesTheDatabaseNorTheBroker()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var worker = scenario.StartWorker(new Dictionary<string, string?>
        {
            ["Postgres:Port"] = "1",
            ["RabbitMq:Port"] = "1"
        });
        using var client = worker.CreateClient();

        (await StatusAsync(client, "/health/live")).ShouldBe((HttpStatusCode.OK, "Healthy"));
    }

    [DockerFact]
    public async Task Live_WhenAHeartbeatIsStale_ReturnsServiceUnavailableWithRetryAfter()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var worker = scenario.StartWorker(
            configureServices: services => services.Decorate<IWorkerHeartbeat>((_, provider) =>
                new StaleHeartbeat(provider.GetRequiredService<TimeProvider>(), WorkerLoop.IntegrityRecent)));
        using var client = worker.CreateClient();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(5));
        (await StatusOfAsync(response)).ShouldBe("Unhealthy");
    }

    [DockerFact]
    public async Task Ready_WhenTheOldestPendingMessageIsOlderThanSixtySeconds_IsDegradedWithStatus200()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var worker = scenario.StartWorker(new Dictionary<string, string?> { ["Outbox:MeasureIntervalSeconds"] = "300" });
        using var client = worker.CreateClient();
        var stats = worker.Services.GetRequiredService<OutboxStatsHolder>();

        await ConditionWait.UntilAsync(() => stats.Snapshot is not null, Patience, "the first measurement");
        await ConditionWait.UntilAsync(
            async () => (await StatusAsync(client, "/health/ready")).State == "Healthy",
            Patience,
            "readiness to become healthy");

        stats.RecordStats(new OutboxStats(10, 61, 0));

        (await StatusAsync(client, "/health/ready")).ShouldBe((HttpStatusCode.OK, "Degraded"));

        stats.RecordStats(new OutboxStats(10, 59, 0));

        (await StatusAsync(client, "/health/ready")).ShouldBe((HttpStatusCode.OK, "Healthy"));
    }

    [DockerFact]
    public async Task Ready_WhenTheBrokerCircuitIsNotClosed_IsDegradedWithStatus200()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var worker = scenario.StartWorker(
            configureServices: services => services.Decorate<IEventPublisher>((inner, _) =>
                new CircuitOverridePublisher(inner, BrokerCircuitState.Open)));
        using var client = worker.CreateClient();

        await ConditionWait.UntilAsync(
            async () => (await StatusAsync(client, "/health/ready")).State != "Unhealthy",
            Patience,
            "readiness to stop being unhealthy");

        (await StatusAsync(client, "/health/ready")).ShouldBe((HttpStatusCode.OK, "Degraded"));
    }

    [DockerFact]
    public async Task Ready_FallsToServiceUnavailableAsSoonAsTheHostIsAskedToStop()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var worker = scenario.StartWorker();
        using var client = worker.CreateClient();

        await ConditionWait.UntilAsync(
            async () => (await StatusAsync(client, "/health/ready")).State == "Healthy",
            Patience,
            "readiness to become healthy");

        worker.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(5));
        (await StatusOfAsync(response)).ShouldBe("Unhealthy");
    }

    [DockerFact]
    public async Task Ready_WhenTheDatabaseIsUnreachableButTheBrokerIsUp_IsServiceUnavailableWhileLiveStaysOk()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var worker = scenario.StartWorker(new Dictionary<string, string?> { ["Postgres:Port"] = "1" });
        using var client = worker.CreateClient();

        using var ready = await client.GetAsync("/health/ready", CancellationToken.None);

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        ready.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(5));
        (await StatusAsync(client, "/health/live")).ShouldBe((HttpStatusCode.OK, "Healthy"));
    }

    [DockerFact]
    public async Task Ready_WhenTheSchemaIsBehindTheCode_ReturnsServiceUnavailable()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);

        var settings = MessagingSettings.For(postgres, database, broker);

        await using var worker = new MessagingWorkerFactory(settings).Started();
        using var client = worker.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await StatusOfAsync(response)).ShouldBe("Unhealthy");
    }

    private static async Task<(HttpStatusCode Status, string? State)> StatusAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route, CancellationToken.None);

        return (response.StatusCode, await StatusOfAsync(response));
    }

    private static async Task<string?> StatusOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);

        return document.RootElement.GetProperty("status").GetString();
    }

    private sealed class StaleHeartbeat(TimeProvider timeProvider, WorkerLoop staleLoop) : IWorkerHeartbeat
    {
        public void Beat(WorkerLoop loop)
        {
        }

        public DateTimeOffset? LastBeat(WorkerLoop loop) =>
            loop == staleLoop ? timeProvider.GetUtcNow() - TimeSpan.FromHours(2) : timeProvider.GetUtcNow();
    }

    private sealed class CircuitOverridePublisher(IEventPublisher inner, BrokerCircuitState circuit) : IEventPublisher
    {
        public BrokerCircuitState Circuit => circuit;

        public bool IsConnected => inner.IsConnected;

        public int ClaimBudget(int configuredBatchSize) => 0;

        public Task<bool> TryConnectAsync(CancellationToken cancellationToken) => inner.TryConnectAsync(cancellationToken);

        public Task<bool> ProbeAsync(CancellationToken cancellationToken) => inner.ProbeAsync(cancellationToken);

        public Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken) =>
            inner.PublishAsync(envelope, cancellationToken);
    }
}
