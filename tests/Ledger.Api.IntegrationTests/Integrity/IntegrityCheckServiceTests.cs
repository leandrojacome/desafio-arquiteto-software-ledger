extern alias LedgerWorker;

using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;
using Ledger.Application.Integrity;
using Ledger.Application.Outbox;
using Ledger.Infrastructure.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using IntegrityCheckService = LedgerWorker::Ledger.Worker.IntegrityCheckService;
using IntegrityOptions = LedgerWorker::Ledger.Worker.IntegrityOptions;

namespace Ledger.Api.IntegrationTests.Integrity;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class IntegrityCheckServiceTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    [DockerFact]
    public async Task TheService_RunsTheRecentRunAtOnceAndEveryIntervalAfterIt_AndTheFullRunOnlyOnceWhileNotDue()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        await using var host = ServiceHost.Create(harness, sessions => sessions);

        await harness.Ledger.SeedAsync();
        await host.Service.StartAsync(CancellationToken.None);

        await ConditionWait.UntilAsync(
            async () => await CountAsync(harness, "RECENT") == 1 && await CountAsync(harness, "FULL") == 1,
            Patience,
            "the first recent run and the first full run");

        host.Time.Advance(FiveMinutes);

        await ConditionWait.UntilAsync(
            async () => await CountAsync(harness, "RECENT") == 2,
            Patience,
            "the second recent run");

        host.Time.Advance(FiveMinutes);

        await ConditionWait.UntilAsync(
            async () => await CountAsync(harness, "RECENT") == 3,
            Patience,
            "the third recent run");

        (await CountAsync(harness, "FULL")).ShouldBe(1);

        await host.Service.StopAsync(CancellationToken.None);
    }

    [DockerFact]
    public async Task TheService_BeatsTheHeartbeatEvenWhenTheRunIsSkippedBecauseAnotherInstanceHoldsTheLock()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        await using var host = ServiceHost.Create(harness, sessions => sessions);

        var holder = await harness.Sessions.TryBeginRunAsync(IntegrityMode.Recent, CancellationToken.None);

        holder.ShouldNotBeNull();

        host.Time.Advance(TimeSpan.FromMinutes(10));

        var started = host.Time.GetUtcNow();

        await host.Service.StartAsync(CancellationToken.None);

        await ConditionWait.UntilAsync(
            () => host.Heartbeat.LastBeat(WorkerLoop.IntegrityRecent) == started,
            Patience,
            "the heartbeat after the skipped run");

        (await CountAsync(harness, "RECENT")).ShouldBe(0);

        await host.Service.StopAsync(CancellationToken.None);
        await holder.DisposeAsync();
    }

    [DockerFact]
    public async Task TheService_SurvivesARunThatFails_LogsEvent4003_BeatsTheHeartbeatAndRunsTheNextRunOnTime()
    {
        FaultyIntegritySessions? faulty = null;

        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        await using var host = ServiceHost.Create(harness, sessions => faulty = new FaultyIntegritySessions(sessions));

        faulty.ShouldNotBeNull();

        var failures = 0;

        faulty.BeforeCheckHeads = () =>
        {
            failures++;

            return failures == 1 ? throw new InvalidOperationException("simulated outage") : Task.CompletedTask;
        };

        await harness.Ledger.SeedAsync();

        var started = host.Time.GetUtcNow();

        await host.Service.StartAsync(CancellationToken.None);

        await ConditionWait.UntilAsync(
            () => host.Log.Events.Any(captured => captured.Id == 4003),
            Patience,
            "the failure to be logged");

        var failed = host.Log.Events.First(captured => captured.Id == 4003);

        failed.Level.ShouldBe(LogLevel.Warning);
        failed.Properties["Mode"].ShouldBe("RECENT");
        failed.Properties["ExceptionType"].ShouldBe(nameof(InvalidOperationException));
        (await CountAsync(harness, "RECENT")).ShouldBe(0);
        host.Heartbeat.LastBeat(WorkerLoop.IntegrityRecent).ShouldNotBeNull();
        host.Loops.Failed.ShouldBe([WorkerLoop.IntegrityRecent]);
        host.Loops.Succeeded.ShouldNotContain(WorkerLoop.IntegrityRecent);

        host.Time.Advance(FiveMinutes);

        await ConditionWait.UntilAsync(
            async () => await CountAsync(harness, "RECENT") == 1,
            Patience,
            "the next recent run after the failure");

        host.Heartbeat.LastBeat(WorkerLoop.IntegrityRecent).ShouldBe(started + FiveMinutes);
        host.Loops.Succeeded.ShouldContain(WorkerLoop.IntegrityRecent);
        host.Loops.Failed.Count.ShouldBe(1);
        host.Service.ExecuteTask.ShouldNotBeNull().IsFaulted.ShouldBeFalse();

        await host.Service.StopAsync(CancellationToken.None);
    }

    [DockerFact]
    public async Task ARecentRunThatNeverReturns_FailsTheLivenessEvenWhileTheFullLoopKeepsBeating()
    {
        FaultyIntegritySessions? faulty = null;

        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        await using var host = ServiceHost.Create(harness, sessions => faulty = new FaultyIntegritySessions(sessions));

        faulty.ShouldNotBeNull();

        await harness.Ledger.SeedAsync();
        await host.Service.StartAsync(CancellationToken.None);

        await ConditionWait.UntilAsync(
            async () => await CountAsync(harness, "RECENT") == 1 && await CountAsync(harness, "FULL") == 1,
            Patience,
            "the first recent run and the first full run");

        var held = new TaskCompletionSource();

        faulty.RecentRunsWait = held.Task;
        host.Time.Advance(FiveMinutes);

        await ConditionWait.UntilAsync(() => faulty.Opened >= 3, Patience, "the second recent run to start and hang");

        var liveness = new WorkerLivenessHealthCheck(
            host.Heartbeat,
            Options.Create(new WorkerHealthOptions()),
            host.Time);

        for (var step = 0; step < 7; step++)
        {
            host.Time.Advance(FiveMinutes);
            host.Heartbeat.Beat(WorkerLoop.Outbox);

            var expected = host.Time.GetUtcNow();

            await ConditionWait.UntilAsync(
                () => host.Heartbeat.LastBeat(WorkerLoop.IntegrityFull) == expected,
                Patience,
                "the full loop to beat again");
        }

        var result = await liveness.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldNotBeNull().ShouldContain("integrity-recent");
        result.Description.ShouldNotContain("integrity-full");

        held.SetResult();
        await host.Service.StopAsync(CancellationToken.None);
    }

    [DockerTheory]
    [InlineData(24)]
    [InlineData(48)]
    [InlineData(168)]
    public async Task TheFullRun_CoversTheWholeConfiguredIntervalPlusTheOverlap(int fullIntervalHours)
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        await using var host = ServiceHost.Create(
            harness,
            sessions => sessions,
            new IntegrityOptions { FullIntervalHours = fullIntervalHours });

        await host.Service.StartAsync(CancellationToken.None);

        await ConditionWait.UntilAsync(
            async () => await CountAsync(harness, "FULL") == 1,
            Patience,
            "the first full run");

        await host.Service.StopAsync(CancellationToken.None);

        var full = (await harness.Ledger.AuditRowsAsync("integrity.run_completed"))
            .Single(row => row.Details.Contains("\"mode\": \"FULL\"", StringComparison.Ordinal));

        using var document = System.Text.Json.JsonDocument.Parse(full.Details);
        var start = DateTimeOffset.Parse(document.RootElement.GetProperty("windowStart").GetString() ?? string.Empty, System.Globalization.CultureInfo.InvariantCulture);
        var end = DateTimeOffset.Parse(document.RootElement.GetProperty("windowEnd").GetString() ?? string.Empty, System.Globalization.CultureInfo.InvariantCulture);

        (end - start).ShouldBe(TimeSpan.FromHours(fullIntervalHours) + TimeSpan.FromMinutes(1));
    }

    [DockerFact]
    public async Task StoppingTheService_EndsBothLoopsWithoutAnExceptionEvenWhileTheyWait()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        await using var host = ServiceHost.Create(harness, sessions => sessions);

        await host.Service.StartAsync(CancellationToken.None);

        await ConditionWait.UntilAsync(
            async () => await CountAsync(harness, "RECENT") == 1,
            Patience,
            "the first run");

        await host.Service.StopAsync(CancellationToken.None);

        host.Service.ExecuteTask.ShouldNotBeNull().IsCompletedSuccessfully.ShouldBeTrue();
    }

    private static async Task<int> CountAsync(IntegrityHarness harness, string mode)
    {
        var rows = await harness.Ledger.AuditRowsAsync("integrity.run_completed");

        return rows.Count(row => row.Details.Contains($"\"mode\": \"{mode}\"", StringComparison.Ordinal));
    }

    private sealed class ServiceHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        private ServiceHost(
            ServiceProvider provider,
            IntegrityCheckService service,
            FakeTimeProvider time,
            WorkerHeartbeat heartbeat,
            RecordingWorkerLoopTelemetry loops,
            LogCapture<IntegrityCheckService> log)
        {
            _provider = provider;
            Service = service;
            Time = time;
            Heartbeat = heartbeat;
            Loops = loops;
            Log = log;
        }

        public IntegrityCheckService Service { get; }

        public FakeTimeProvider Time { get; }

        public WorkerHeartbeat Heartbeat { get; }

        public RecordingWorkerLoopTelemetry Loops { get; }

        public LogCapture<IntegrityCheckService> Log { get; }

        public static ServiceHost Create(
            IntegrityHarness harness,
            Func<IIntegritySessions, IIntegritySessions> decorate,
            IntegrityOptions? options = null)
        {
            var time = new FakeTimeProvider();
            var heartbeat = new WorkerHeartbeat(time);
            var loops = new RecordingWorkerLoopTelemetry();
            var log = new LogCapture<IntegrityCheckService>();
            var services = new ServiceCollection();

            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<IWorkerHeartbeat>(heartbeat);
            services.AddSingleton(decorate(harness.Sessions));
            services.AddSingleton<IIntegrityTelemetry>(harness.Telemetry);
            services.AddSingleton<IIdGenerator, SequentialIds>();
            services.AddSingleton<ILogger<RunIntegrityCheckHandler>>(harness.HandlerLog);
            services.AddScoped<RunIntegrityCheckHandler>();

            var provider = services.BuildServiceProvider();
            var service = new IntegrityCheckService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(options ?? new IntegrityOptions()),
                heartbeat,
                loops,
                time,
                log);

            return new ServiceHost(provider, service, time, heartbeat, loops, log);
        }

        public ValueTask DisposeAsync()
        {
            Service.Dispose();

            return _provider.DisposeAsync();
        }
    }

    private sealed class SequentialIds : IIdGenerator
    {
        public Guid NewId() => Guid.CreateVersion7();
    }
}
