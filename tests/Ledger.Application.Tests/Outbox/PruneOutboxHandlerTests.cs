using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Ledger.Application.Tests.Support;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Application.Tests.Outbox;

[Trait("Category", "Unit")]
public sealed class PruneOutboxHandlerTests
{
    private IOutboxQueue Queue { get; } = Substitute.For<IOutboxQueue>();

    private RecordingOutboxTelemetry Telemetry { get; } = new();

    private CapturingLogger<PruneOutboxHandler> Logger { get; } = new();

    private PruneOutboxHandler Handler =>
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
                5000),
            Logger);

    [Fact]
    public async Task HandleAsync_FullBatchesFollowedByAShortOne_RepeatsUntilTheFirstIncompleteBatch()
    {
        Queue.PruneAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(5000, 5000, 1200);

        var removed = await Handler.HandleAsync(CancellationToken.None);

        removed.ShouldBe(11_200);
        await Queue.Received(3).PruneAsync(TimeSpan.FromDays(7), 5000, Arg.Any<CancellationToken>());
        Telemetry.Pruned.ShouldBe([11_200]);
    }

    [Fact]
    public async Task HandleAsync_LogsTheRemovedCountAtInformation()
    {
        Queue.PruneAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(5000, 1200);

        await Handler.HandleAsync(CancellationToken.None);

        var log = Logger.Single(3004);
        log.Level.ShouldBe(LogLevel.Information);
        log.Properties["Removed"].ShouldBe("6200");
    }

    [Fact]
    public async Task HandleAsync_NothingToRemove_LogsAtDebugAndCountsNothing()
    {
        Queue.PruneAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(0);

        var removed = await Handler.HandleAsync(CancellationToken.None);

        removed.ShouldBe(0);
        await Queue.Received(1).PruneAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        Telemetry.Pruned.ShouldBeEmpty();
        Logger.Single(3004).Level.ShouldBe(LogLevel.Debug);
    }

    [Fact]
    public async Task HandleAsync_ExactMultipleOfTheBatch_StopsOnTheFollowingEmptyBatch()
    {
        Queue.PruneAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(5000, 0);

        var removed = await Handler.HandleAsync(CancellationToken.None);

        removed.ShouldBe(5000);
        await Queue.Received(2).PruneAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CanceledBetweenBatches_StopsWithoutPruningAgain()
    {
        using var source = new CancellationTokenSource();
        Queue.PruneAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await source.CancelAsync();

                return 5000;
            });

        await Should.ThrowAsync<OperationCanceledException>(() => Handler.HandleAsync(source.Token));

        await Queue.Received(1).PruneAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_QueueFails_TheExceptionPropagates()
    {
        Queue.PruneAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(() => Handler.HandleAsync(CancellationToken.None));
    }
}
