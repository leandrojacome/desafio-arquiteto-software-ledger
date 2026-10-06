using Ledger.Infrastructure.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Persistence;

[Trait("Category", "Unit")]
public sealed class CachedHealthCheckTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private static readonly HealthCheckContext Context = new();

    [Fact]
    public async Task CheckHealthAsync_WithinTheWindow_ReusesTheLastResult()
    {
        var time = new FakeTimeProvider();
        var probe = new CountingCheck(HealthCheckResult.Healthy());
        var cached = new CachedHealthCheck(probe, Window, time);

        await cached.CheckHealthAsync(Context, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(4));
        var second = await cached.CheckHealthAsync(Context, CancellationToken.None);

        probe.Calls.ShouldBe(1);
        second.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_AfterTheWindow_ProbesAgain()
    {
        var time = new FakeTimeProvider();
        var probe = new CountingCheck(HealthCheckResult.Healthy());
        var cached = new CachedHealthCheck(probe, Window, time);

        await cached.CheckHealthAsync(Context, CancellationToken.None);
        time.Advance(Window);
        await cached.CheckHealthAsync(Context, CancellationToken.None);

        probe.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task CheckHealthAsync_ACachedFailure_StaysInCacheForTheWindow()
    {
        var time = new FakeTimeProvider();
        var probe = new CountingCheck(HealthCheckResult.Unhealthy("down"));
        var cached = new CachedHealthCheck(probe, Window, time);

        await cached.CheckHealthAsync(Context, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1));
        var second = await cached.CheckHealthAsync(Context, CancellationToken.None);

        probe.Calls.ShouldBe(1);
        second.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_FiftyConcurrentCallers_ShareASingleProbe()
    {
        var time = new FakeTimeProvider();
        var probe = new GatedCheck();
        var cached = new CachedHealthCheck(probe, Window, time);

        var callers = Enumerable.Range(0, 50)
            .Select(_ => cached.CheckHealthAsync(Context, CancellationToken.None))
            .ToArray();

        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        probe.Release();
        var results = await Task.WhenAll(callers);

        probe.Calls.ShouldBe(1);
        results.ShouldAllBe(result => result.Status == HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenOneCallerCancels_TheSharedProbeKeepsRunningForTheOthers()
    {
        var time = new FakeTimeProvider();
        var probe = new GatedCheck();
        var cached = new CachedHealthCheck(probe, Window, time);
        using var cancellation = new CancellationTokenSource();

        var impatient = cached.CheckHealthAsync(Context, cancellation.Token);
        var patient = cached.CheckHealthAsync(Context, CancellationToken.None);

        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => impatient);

        probe.Release();
        var result = await patient;

        result.Status.ShouldBe(HealthStatus.Healthy);
        probe.ReceivedCancelableToken.ShouldBeFalse();
        probe.Calls.ShouldBe(1);
    }

    private sealed class CountingCheck(HealthCheckResult result) : IHealthCheck
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);

            return Task.FromResult(result);
        }
    }

    private sealed class GatedCheck : IHealthCheck
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _calls;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public bool ReceivedCancelableToken { get; private set; }

        public void Release() => _gate.SetResult();

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            ReceivedCancelableToken = cancellationToken.CanBeCanceled;
            Started.TrySetResult();

            await _gate.Task;

            return HealthCheckResult.Healthy();
        }
    }
}
