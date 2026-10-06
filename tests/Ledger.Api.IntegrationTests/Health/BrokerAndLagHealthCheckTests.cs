using Ledger.Application.Outbox;
using Ledger.Infrastructure.Health;
using Ledger.Infrastructure.Messaging;
using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Health;

[Trait("Category", "Unit")]
public sealed class BrokerAndLagHealthCheckTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly OutboxStatsHolder _stats;
    private readonly OutboxLagHealthCheck _lag;

    public BrokerAndLagHealthCheckTests()
    {
        _stats = new OutboxStatsHolder(_time, Options.Create(new OutboxOptions { MeasureIntervalSeconds = 10 }));
        _lag = new OutboxLagHealthCheck(_stats, Options.Create(new WorkerHealthOptions()), _time);
    }

    [Fact]
    public async Task OutboxLag_WithoutASnapshotJustAfterTheStart_IsHealthy()
    {
        _time.Advance(TimeSpan.FromSeconds(60));

        (await _lag.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None)).Status
            .ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task OutboxLag_WithoutAnyMeasurementAMinuteAfterTheStart_IsDegraded()
    {
        _time.Advance(TimeSpan.FromSeconds(61));

        var result = await _lag.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task OutboxLag_WhenTheMeasurementStopsArriving_BecomesDegradedAfterThreeIntervals()
    {
        _stats.RecordStats(new OutboxStats(10, 1d, 0));

        _time.Advance(TimeSpan.FromSeconds(30));
        (await _lag.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None)).Status
            .ShouldBe(HealthStatus.Healthy);

        _time.Advance(TimeSpan.FromSeconds(1));
        var stale = await _lag.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        stale.Status.ShouldBe(HealthStatus.Degraded);
        stale.Description.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task OutboxLag_AfterAFreshMeasurementFollowingAStaleOne_IsHealthyAgain()
    {
        _stats.RecordStats(new OutboxStats(10, 1d, 0));
        _time.Advance(TimeSpan.FromMinutes(5));
        _stats.RecordStats(new OutboxStats(10, 1d, 0));

        (await _lag.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None)).Status
            .ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public void OutboxStats_FreshSnapshotDisappearsWhenTheMeasurementIsStale_ButTheLastValueIsKept()
    {
        _stats.RecordStats(new OutboxStats(10, 1d, 2));

        _stats.FreshSnapshot.ShouldNotBeNull().Failed.ShouldBe(2);

        _time.Advance(TimeSpan.FromSeconds(31));

        _stats.FreshSnapshot.ShouldBeNull();
        _stats.Snapshot.ShouldNotBeNull().Pending.ShouldBe(10);
    }

    [Theory]
    [InlineData(0d, HealthStatus.Healthy)]
    [InlineData(60d, HealthStatus.Healthy)]
    [InlineData(60.5d, HealthStatus.Degraded)]
    [InlineData(3600d, HealthStatus.Degraded)]
    public async Task OutboxLag_ComparesTheOldestPendingAgeWithTheLimit(double age, HealthStatus expected)
    {
        _stats.RecordStats(new OutboxStats(10, age, 0));

        (await _lag.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None)).Status.ShouldBe(expected);
    }

    [Fact]
    public async Task OutboxLag_WithNoPendingMessage_IsHealthy()
    {
        _stats.RecordStats(new OutboxStats(0, null, 0));

        (await _lag.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None)).Status
            .ShouldBe(HealthStatus.Healthy);
    }

    [Theory]
    [InlineData(BrokerCircuitState.Closed, HealthStatus.Healthy)]
    [InlineData(BrokerCircuitState.HalfOpen, HealthStatus.Degraded)]
    [InlineData(BrokerCircuitState.Open, HealthStatus.Degraded)]
    public async Task BrokerCircuit_IsDegradedUnlessClosed(BrokerCircuitState state, HealthStatus expected)
    {
        var publisher = new StateOnlyPublisher(state);
        var check = new BrokerCircuitHealthCheck(publisher);

        (await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None)).Status.ShouldBe(expected);
    }

    [Fact]
    public async Task Shutdown_IsHealthyWhileTheHostRuns()
    {
        using var lifetime = new FakeLifetime();
        var check = new ShutdownHealthCheck(lifetime);

        (await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None)).Status
            .ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Shutdown_TurnsUnhealthyAsSoonAsTheHostIsAskedToStop()
    {
        using var lifetime = new FakeLifetime();
        var check = new ShutdownHealthCheck(lifetime);

        lifetime.StopApplication();

        (await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None)).Status
            .ShouldBe(HealthStatus.Unhealthy);
    }

    private sealed class StateOnlyPublisher(BrokerCircuitState state) : Ledger.Application.Abstractions.IEventPublisher
    {
        public BrokerCircuitState Circuit => state;

        public bool IsConnected => true;

        public int ClaimBudget(int configuredBatchSize) => configuredBatchSize;

        public Task<bool> TryConnectAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();

        public void Dispose() => _stopping.Dispose();
    }
}
