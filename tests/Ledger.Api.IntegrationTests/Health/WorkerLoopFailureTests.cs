using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog.Core;

namespace Ledger.Api.IntegrationTests.Health;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class WorkerLoopFailureTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task Ready_WhenTheMeasurementFailsOnEveryCycle_TurnsDegradedAfterTheFirstMinuteAndTheFailuresAreCounted()
    {
        using var failures = new MeterCounter("Ledger", "ledger.worker.loop.failures");
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var clock = new SkewedTimeProvider();
        var worker = scenario.StartWorker(
            new Dictionary<string, string?> { ["Outbox:MeasureIntervalSeconds"] = "1" },
            services =>
            {
                services.Decorate<IOutboxQueue>((inner, _) => new FailingQueue(inner, failStats: true, failPrune: false));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        var health = worker.Services.GetRequiredService<HealthCheckService>();

        await ConditionWait.UntilAsync(() => failures.Total >= 2, Patience, "the measurement to fail twice");

        (await OutboxLagAsync(health)).ShouldBe(HealthStatus.Healthy);

        clock.Skew = TimeSpan.FromSeconds(61);

        (await OutboxLagAsync(health)).ShouldBe(HealthStatus.Degraded);

        using var client = worker.CreateClient();
        using var ready = await client.GetAsync("/health/ready", CancellationToken.None);

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync(CancellationToken.None)).ShouldBe("{\"status\":\"Degraded\"}");
    }

    [DockerFact]
    public async Task APruneThatFailsOnEveryCycle_IsCountedAndLogged_WhileTheProcessStaysAliveAndLiveStaysOk()
    {
        using var failures = new MeterCounter("Ledger", "ledger.worker.loop.failures");
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        var sink = new WorkerLogSink();
        var worker = scenario.StartWorker(
            new Dictionary<string, string?> { ["Serilog:MinimumLevel:Default"] = "Information" },
            services =>
            {
                services.Decorate<IOutboxQueue>((inner, _) => new FailingQueue(inner, failStats: false, failPrune: true));
                services.AddSingleton<ILogEventSink>(sink);
            });
        using var client = worker.CreateClient();

        await ConditionWait.UntilAsync(() => failures.Total >= 2, Patience, "the prune to fail twice");

        var logged = sink.WithId(3008).First(captured => (string)captured.Properties["Loop"] == "prune");

        logged.Properties["ExceptionType"].ShouldBe(nameof(InvalidOperationException));
        using var live = await client.GetAsync("/health/live", CancellationToken.None);
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<HealthStatus> OutboxLagAsync(HealthCheckService health)
    {
        var report = await health.CheckHealthAsync(registration => registration.Name == "outbox-lag", CancellationToken.None);

        return report.Entries["outbox-lag"].Status;
    }

    private sealed class SkewedTimeProvider : TimeProvider
    {
        private long _skewTicks;

        public TimeSpan Skew
        {
            get => TimeSpan.FromTicks(Interlocked.Read(ref _skewTicks));
            set => Interlocked.Exchange(ref _skewTicks, value.Ticks);
        }

        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + Skew;
    }

    private sealed class FailingQueue(IOutboxQueue inner, bool failStats, bool failPrune) : IOutboxQueue
    {
        public Task<IReadOnlyList<OutboxEnvelope>> ClaimBatchAsync(
            int batchSize,
            TimeSpan lease,
            CancellationToken cancellationToken) => inner.ClaimBatchAsync(batchSize, lease, cancellationToken);

        public Task<int> MarkPublishedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            inner.MarkPublishedAsync(ids, cancellationToken);

        public Task<int> ReleaseAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            inner.ReleaseAsync(ids, cancellationToken);

        public Task<int> PruneAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken) =>
            failPrune
                ? Task.FromException<int>(new InvalidOperationException("Simulated prune failure."))
                : inner.PruneAsync(retention, batchSize, cancellationToken);

        public Task<OutboxStats> ReadStatsAsync(OutboxStatsRequest request, CancellationToken cancellationToken) =>
            failStats
                ? Task.FromException<OutboxStats>(new InvalidOperationException("Simulated measurement failure."))
                : inner.ReadStatsAsync(request, cancellationToken);
    }
}
