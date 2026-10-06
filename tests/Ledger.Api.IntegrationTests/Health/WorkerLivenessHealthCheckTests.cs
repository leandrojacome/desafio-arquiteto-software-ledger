using Ledger.Application.Outbox;
using Ledger.Infrastructure.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Health;

[Trait("Category", "Unit")]
public sealed class WorkerLivenessHealthCheckTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly WorkerHeartbeat _heartbeat;
    private readonly WorkerLivenessHealthCheck _check;

    public WorkerLivenessHealthCheckTests()
    {
        _heartbeat = new WorkerHeartbeat(_time);
        _check = new WorkerLivenessHealthCheck(_heartbeat, Options.Create(new WorkerHealthOptions()), _time);
    }

    [Fact]
    public async Task AFreshlyStartedWorker_IsHealthy()
    {
        (await RunAsync()).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task OutboxLoopSilentFor119Seconds_IsStillHealthy()
    {
        _time.Advance(TimeSpan.FromSeconds(119));
        _heartbeat.Beat(WorkerLoop.IntegrityRecent);

        (await RunAsync()).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task OutboxLoopSilentFor121Seconds_IsUnhealthy()
    {
        _time.Advance(TimeSpan.FromSeconds(121));
        _heartbeat.Beat(WorkerLoop.IntegrityRecent);

        (await RunAsync()).Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task IntegrityLoopSilentFor29Minutes_IsStillHealthy()
    {
        for (var minute = 0; minute < 29; minute++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            _heartbeat.Beat(WorkerLoop.Outbox);
        }

        (await RunAsync()).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task IntegrityLoopSilentFor31Minutes_IsUnhealthy()
    {
        for (var minute = 0; minute < 31; minute++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            _heartbeat.Beat(WorkerLoop.Outbox);
        }

        (await RunAsync()).Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task ANewBeat_RestoresHealth()
    {
        _time.Advance(TimeSpan.FromMinutes(40));

        (await RunAsync()).Status.ShouldBe(HealthStatus.Unhealthy);

        _heartbeat.Beat(WorkerLoop.Outbox);
        _heartbeat.Beat(WorkerLoop.IntegrityRecent);
        _heartbeat.Beat(WorkerLoop.IntegrityFull);

        (await RunAsync()).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task RecentIntegrityLoopSilentFor31Minutes_IsUnhealthyEvenWhileTheFullLoopKeepsBeating()
    {
        for (var minute = 0; minute < 31; minute++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            _heartbeat.Beat(WorkerLoop.Outbox);
            _heartbeat.Beat(WorkerLoop.IntegrityFull);
        }

        var result = await RunAsync();

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldNotBeNull().ShouldContain("integrity-recent");
        result.Description.ShouldNotContain("integrity-full");
    }

    [Fact]
    public async Task FullIntegrityLoopSilentFor31Minutes_IsUnhealthyEvenWhileTheRecentLoopKeepsBeating()
    {
        for (var minute = 0; minute < 31; minute++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            _heartbeat.Beat(WorkerLoop.Outbox);
            _heartbeat.Beat(WorkerLoop.IntegrityRecent);
        }

        var result = await RunAsync();

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldNotBeNull().ShouldContain("integrity-full");
    }

    [Fact]
    public async Task TheAuxiliaryLoops_NeverMakeTheProcessUnhealthyBecauseTheyAreSilent()
    {
        for (var minute = 0; minute < 240; minute++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            _heartbeat.Beat(WorkerLoop.Outbox);
            _heartbeat.Beat(WorkerLoop.IntegrityRecent);
            _heartbeat.Beat(WorkerLoop.IntegrityFull);
        }

        _heartbeat.LastBeat(WorkerLoop.OutboxPrune).ShouldNotBeNull();
        _heartbeat.LastBeat(WorkerLoop.Measure).ShouldNotBeNull();
        _heartbeat.LastBeat(WorkerLoop.IdempotencyPrune).ShouldNotBeNull();
        _heartbeat.LastBeat(WorkerLoop.KeyRewrap).ShouldNotBeNull();
        (await RunAsync()).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task TheLimitsComeFromTheOptions()
    {
        var strict = new WorkerLivenessHealthCheck(
            _heartbeat,
            Options.Create(new WorkerHealthOptions { OutboxHeartbeatSeconds = 10 }),
            _time);

        _time.Advance(TimeSpan.FromSeconds(11));
        _heartbeat.Beat(WorkerLoop.IntegrityRecent);

        (await strict.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None)).Status
            .ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public void TheCheck_DependsOnNothingButTheHeartbeatTheOptionsAndTheClock()
    {
        var constructor = typeof(WorkerLivenessHealthCheck).GetConstructors().ShouldHaveSingleItem();

        constructor.GetParameters().Select(parameter => parameter.ParameterType).ShouldBe(
        [
            typeof(Ledger.Application.Abstractions.IWorkerHeartbeat),
            typeof(IOptions<WorkerHealthOptions>),
            typeof(TimeProvider)
        ]);
    }

    private Task<HealthCheckResult> RunAsync() => _check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
}
