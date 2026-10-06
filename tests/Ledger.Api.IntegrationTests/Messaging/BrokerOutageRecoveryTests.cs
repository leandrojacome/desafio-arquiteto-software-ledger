using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Messaging;

[Trait("Category", "Unit")]
public sealed class BrokerOutageRecoveryTests
{
    private static readonly TimeSpan Break = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryOutboxQueue _queue;
    private readonly ScriptedEventPublisher _broker = new();
    private readonly RecordingOutboxTelemetry _telemetry = new();
    private readonly CircuitBreakingEventPublisher _circuit;
    private readonly PublishOutboxBatchHandler _handler;
    private readonly OutboxSettings _settings = new(
        200,
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(5),
        5,
        1000,
        1_000_000,
        TimeSpan.FromDays(7),
        5000);

    public BrokerOutageRecoveryTests()
    {
        _queue = new InMemoryOutboxQueue(_time);
        _circuit = new CircuitBreakingEventPublisher(
            _broker,
            Options.Create(new BrokerCircuitOptions()),
            _telemetry,
            _time,
            NullLogger<CircuitBreakingEventPublisher>.Instance);
        _handler = new PublishOutboxBatchHandler(
            _queue,
            _circuit,
            _telemetry,
            _settings,
            _time,
            NullLogger<PublishOutboxBatchHandler>.Instance);
    }

    [Fact]
    public async Task ABrokerOutageOfFiveMinutes_NeverChargesAnAttemptToTheMessagesThatWaitedForIt()
    {
        var ids = Enqueue(30);

        BrokerGoesDown();

        for (var second = 0; second < 300; second++)
        {
            await _handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

            _time.Advance(TimeSpan.FromSeconds(1));
        }

        var stats = await _queue.ReadStatsAsync(new OutboxStatsRequest(1_000_000, 5, 1000), CancellationToken.None);

        _circuit.Circuit.ShouldNotBe(BrokerCircuitState.Closed);
        _queue.Snapshot().ShouldAllBe(row => row.Attempts == 0 && !row.Published);
        stats.Failed.ShouldBe(0);
        stats.Pending.ShouldBe(ids.Count);
    }

    [Fact]
    public async Task WhenTheBrokerComesBack_TheNextCycleProbesClosesTheCircuitAndPublishesEverythingAtOnce()
    {
        Enqueue(30);
        BrokerGoesDown();

        for (var second = 0; second < 120; second++)
        {
            await _handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

            _time.Advance(TimeSpan.FromSeconds(1));
        }

        BrokerComesBack();

        var outcome = await _handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.WasClaimed.ShouldBeTrue();
        outcome.Confirmed.ShouldBe(30);
        _circuit.Circuit.ShouldBe(BrokerCircuitState.Closed);
        _queue.Snapshot().ShouldAllBe(row => row.Published);
    }

    [Fact]
    public async Task AMessageTheBrokerAlwaysRejects_NeverHoldsTheOthersBackAfterTheOutage()
    {
        var poison = Enqueue(1, firstSecond: 0)[0];
        var healthy = Enqueue(25, firstSecond: 1);

        BrokerGoesDown();

        for (var second = 0; second < 120; second++)
        {
            await _handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

            _time.Advance(TimeSpan.FromSeconds(1));
        }

        _broker.Behavior = (envelope, _) => envelope.Id == poison
            ? ScriptedEventPublisher.Fail(PublishFailureReason.Nack, envelope)
            : Task.CompletedTask;
        _broker.ProbeBehavior = _ => Task.FromResult(true);

        for (var cycle = 0; cycle < 3; cycle++)
        {
            await _handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

            _time.Advance(TimeSpan.FromSeconds(1));
        }

        var rows = _queue.Snapshot();

        rows.Where(row => healthy.Contains(row.Id)).ShouldAllBe(row => row.Published);
        rows.Single(row => row.Id == poison).Published.ShouldBeFalse();
        rows.Single(row => row.Id == poison).Attempts.ShouldBeGreaterThanOrEqualTo(1);
        _circuit.Circuit.ShouldBe(BrokerCircuitState.Closed);
    }

    [Fact]
    public async Task AProbeThatTheBrokerDoesNotConfirm_KeepsTheCircuitOpenAndClaimsNothing()
    {
        Enqueue(5);
        BrokerGoesDown();

        for (var second = 0; second < 60; second++)
        {
            await _handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

            _time.Advance(TimeSpan.FromSeconds(1));
        }

        var callsBefore = _broker.Calls;
        var outcome = await _handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.WasClaimed.ShouldBeFalse();
        _broker.Calls.ShouldBe(callsBefore);
        _circuit.Circuit.ShouldBe(BrokerCircuitState.Open);
        _queue.Snapshot().ShouldAllBe(row => row.Attempts == 0);
    }

    [Fact]
    public async Task AnIdleInstanceWithAnOpenCircuit_ClosesItByProbingEvenWithNothingToPublish()
    {
        Enqueue(12);
        BrokerGoesDown();

        for (var second = 0; second < 20; second++)
        {
            await _handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);
        }

        _circuit.Circuit.ShouldBe(BrokerCircuitState.Open);

        BrokerComesBack();

        var drained = await _queue.ClaimBatchAsync(200, TimeSpan.FromSeconds(1), CancellationToken.None);
        await _queue.MarkPublishedAsync([.. drained.Select(envelope => envelope.Id)], CancellationToken.None);
        _time.Advance(Break);

        var outcome = await _handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        _circuit.Circuit.ShouldBe(BrokerCircuitState.Closed);
        outcome.WasClaimed.ShouldBeTrue();
        outcome.Claimed.ShouldBe(0);
    }

    private void BrokerGoesDown()
    {
        _broker.Behavior = (envelope, _) => ScriptedEventPublisher.Fail(PublishFailureReason.BrokerUnavailable, envelope);
        _broker.ProbeBehavior = _ => Task.FromResult(false);
    }

    private void BrokerComesBack()
    {
        _broker.Behavior = (_, _) => Task.CompletedTask;
        _broker.ProbeBehavior = _ => Task.FromResult(true);
    }

    private List<Guid> Enqueue(int count, int firstSecond = 0)
    {
        var ids = new List<Guid>(count);

        for (var index = 0; index < count; index++)
        {
            var id = Guid.CreateVersion7();

            _queue.Add(new OutboxEnvelope(
                id,
                AccountId.From(Guid.NewGuid()).Value,
                "EntryRegistered",
                "{}",
                "correlation",
                null,
                DateTimeOffset.UnixEpoch.AddSeconds(firstSecond + index),
                0));
            ids.Add(id);
        }

        return ids;
    }
}
