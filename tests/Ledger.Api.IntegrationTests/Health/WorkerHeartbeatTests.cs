using Ledger.Application.Outbox;
using Ledger.Infrastructure.Health;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Health;

[Trait("Category", "Unit")]
public sealed class WorkerHeartbeatTests
{
    [Fact]
    public void BothLoops_StartWithTheStartupInstant()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-03T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        var heartbeat = new WorkerHeartbeat(time);

        heartbeat.LastBeat(WorkerLoop.Outbox).ShouldBe(time.GetUtcNow());
        heartbeat.LastBeat(WorkerLoop.IntegrityRecent).ShouldBe(time.GetUtcNow());
    }

    [Fact]
    public void Beat_RecordsTheCurrentInstantOfThatLoopOnly()
    {
        var time = new FakeTimeProvider();
        var heartbeat = new WorkerHeartbeat(time);
        var startedAt = time.GetUtcNow();

        time.Advance(TimeSpan.FromSeconds(42));
        heartbeat.Beat(WorkerLoop.Outbox);

        heartbeat.LastBeat(WorkerLoop.Outbox).ShouldBe(startedAt + TimeSpan.FromSeconds(42));
        heartbeat.LastBeat(WorkerLoop.IntegrityRecent).ShouldBe(startedAt);
    }

    [Fact]
    public void Beat_WithAnUnknownLoop_Throws()
    {
        var heartbeat = new WorkerHeartbeat(new FakeTimeProvider());

        Should.Throw<ArgumentOutOfRangeException>(() => heartbeat.Beat((WorkerLoop)42));
    }

    [Fact]
    public async Task Beat_FromManyThreads_NeverLosesTheMostRecentInstant()
    {
        var time = new FakeTimeProvider();
        var heartbeat = new WorkerHeartbeat(time);

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            for (var index = 0; index < 200; index++)
            {
                heartbeat.Beat(WorkerLoop.Outbox);
            }
        })));

        heartbeat.LastBeat(WorkerLoop.Outbox).ShouldBe(time.GetUtcNow());
    }
}
