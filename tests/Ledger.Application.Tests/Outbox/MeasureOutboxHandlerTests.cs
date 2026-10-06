using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Application.Tests.Outbox;

[Trait("Category", "Unit")]
public sealed class MeasureOutboxHandlerTests
{
    private IOutboxQueue Queue { get; } = Substitute.For<IOutboxQueue>();

    private RecordingOutboxTelemetry Telemetry { get; } = new();

    private MeasureOutboxHandler Handler =>
        new(
            Queue,
            Telemetry,
            new OutboxSettings(
                200,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5),
                5,
                1000,
                1_000_000,
                TimeSpan.FromDays(7),
                5000));

    [Fact]
    public async Task HandleAsync_AsksTheQueueWithTheLimitsOfTheSettings()
    {
        Queue.ReadStatsAsync(Arg.Any<OutboxStatsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OutboxStats(10, 2.5, 1));

        await Handler.HandleAsync(CancellationToken.None);

        await Queue.Received(1).ReadStatsAsync(new OutboxStatsRequest(1_000_000, 5, 1000), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ReportsTheSnapshotToTheTelemetryAndReturnsIt()
    {
        Queue.ReadStatsAsync(Arg.Any<OutboxStatsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OutboxStats(10, 2.5, 1));

        var stats = await Handler.HandleAsync(CancellationToken.None);

        stats.ShouldBe(new OutboxStats(10, 2.5, 1));
        Telemetry.Measured.ShouldBe([new OutboxStats(10, 2.5, 1)]);
    }

    [Fact]
    public async Task HandleAsync_NoPendingMessage_RecordsAnAgeOfZero()
    {
        Queue.ReadStatsAsync(Arg.Any<OutboxStatsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OutboxStats(0, null, 0));

        var stats = await Handler.HandleAsync(CancellationToken.None);

        stats.OldestPendingAgeSeconds.ShouldBe(0d);
        Telemetry.Measured.Single().OldestPendingAgeSeconds.ShouldBe(0d);
    }

    [Fact]
    public async Task HandleAsync_QueueFails_TheExceptionPropagatesAndNothingIsReported()
    {
        Queue.ReadStatsAsync(Arg.Any<OutboxStatsRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(() => Handler.HandleAsync(CancellationToken.None));

        Telemetry.Measured.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_TokenReachesTheQueue()
    {
        Queue.ReadStatsAsync(Arg.Any<OutboxStatsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OutboxStats(0, null, 0));
        using var source = new CancellationTokenSource();

        await Handler.HandleAsync(source.Token);

        await Queue.Received(1).ReadStatsAsync(Arg.Any<OutboxStatsRequest>(), source.Token);
    }
}
