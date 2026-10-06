using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Ledger.Application.Tests.Support;
using Ledger.Domain.Accounts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Application.Tests.Outbox;

[Trait("Category", "Unit")]
public sealed class PublishOutboxBatchHandlerTests
{
    private static readonly AccountId Account = AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;
    private static readonly DateTimeOffset Base = new(2026, 10, 1, 14, 0, 0, TimeSpan.Zero);

    private IOutboxQueue Queue { get; } = Substitute.For<IOutboxQueue>();

    private IEventPublisher Publisher { get; } = Substitute.For<IEventPublisher>();

    private RecordingOutboxTelemetry Telemetry { get; } = new();

    private FakeTimeProvider Time { get; } = new(Base);

    private CapturingLogger<PublishOutboxBatchHandler> Logger { get; } = new();

    public PublishOutboxBatchHandlerTests()
    {
        Publisher.IsConnected.Returns(true);
        Publisher.Circuit.Returns(BrokerCircuitState.Closed);
        Publisher.ClaimBudget(Arg.Any<int>()).Returns(200);
        Publisher.TryConnectAsync(Arg.Any<CancellationToken>()).Returns(true);
        Queue.MarkPublishedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(1);
    }

    private PublishOutboxBatchHandler Handler => new(Queue, Publisher, Telemetry, Settings(), Time, Logger);

    private static OutboxSettings Settings(int failedAttempts = 5) =>
        new(
            200,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5),
            failedAttempts,
            1000,
            1_000_000,
            TimeSpan.FromDays(7),
            5000);

    private static OutboxEnvelope Envelope(int number, int attempts = 1, int minutesAfterBase = 0) =>
        new(
            new Guid(number, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]),
            Account,
            "EntryRegistered",
            "{\"payload\":\"secret-value\"}",
            "corr-" + number,
            null,
            Base.AddMinutes(minutesAfterBase),
            attempts);

    private void QueueReturns(params OutboxEnvelope[] batch) =>
        Queue.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(batch);

    private void PublishFails(OutboxEnvelope envelope, PublishFailureReason reason) =>
        Publisher.PublishAsync(Arg.Is<OutboxEnvelope>(candidate => candidate.Id == envelope.Id), Arg.Any<CancellationToken>())
            .ThrowsAsync(new EventPublishException(reason, envelope.Id));

    private static bool SameIds(IReadOnlyCollection<Guid> ids, params OutboxEnvelope[] expected) =>
        ids.Order().SequenceEqual(expected.Select(envelope => envelope.Id).Order());

    [Fact]
    public async Task HandleAsync_BudgetZero_ReturnsNotClaimedAndNeverTouchesTheQueue()
    {
        Publisher.ClaimBudget(Arg.Any<int>()).Returns(0);
        Publisher.Circuit.Returns(BrokerCircuitState.Open);

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.WasClaimed.ShouldBeFalse();
        outcome.Circuit.ShouldBe(BrokerCircuitState.Open);
        await Queue.DidNotReceive().ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        Telemetry.Polls.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_CircuitNotClosed_ProbesTheBrokerBeforeAskingForABudget()
    {
        var closed = false;
        Publisher.Circuit.Returns(_ => closed ? BrokerCircuitState.Closed : BrokerCircuitState.HalfOpen);
        Publisher.ProbeAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            closed = true;

            return Task.FromResult(true);
        });
        QueueReturns(Envelope(1));

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.WasClaimed.ShouldBeTrue();
        Received.InOrder(() =>
        {
            Publisher.ProbeAsync(Arg.Any<CancellationToken>());
            Publisher.ClaimBudget(Arg.Any<int>());
        });
    }

    [Fact]
    public async Task HandleAsync_CircuitNotClosedAndTheProbeIsNotConfirmed_ClaimsNothing()
    {
        Publisher.Circuit.Returns(BrokerCircuitState.Open);
        Publisher.ProbeAsync(Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.WasClaimed.ShouldBeFalse();
        outcome.Circuit.ShouldBe(BrokerCircuitState.Open);
        Publisher.DidNotReceive().ClaimBudget(Arg.Any<int>());
        await Queue.DidNotReceive().ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CircuitClosed_NeverSpendsAProbe()
    {
        QueueReturns(Envelope(1));

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        await Publisher.DidNotReceive().ProbeAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PublishFailureReason.BrokerUnavailable)]
    [InlineData(PublishFailureReason.Timeout)]
    [InlineData(PublishFailureReason.Unroutable)]
    public async Task HandleAsync_FailureAttributableToTheBroker_ReleasesTheMessageInsteadOfChargingAnAttempt(
        PublishFailureReason reason)
    {
        var failing = Envelope(1);
        var confirmed = Envelope(2);
        QueueReturns(failing, confirmed);
        PublishFails(failing, reason);

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        await Queue.Received(1).ReleaseAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, failing)),
            Arg.Any<CancellationToken>());
        await Queue.Received(1).MarkPublishedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, confirmed)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PublishFailureReason.Nack)]
    [InlineData(PublishFailureReason.Serialization)]
    public async Task HandleAsync_NackOrSerializationFailure_KeepsTheAttemptTheClaimCharged(PublishFailureReason reason)
    {
        var failing = Envelope(1);
        QueueReturns(failing);
        PublishFails(failing, reason);

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        await Queue.DidNotReceive().ReleaseAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AnythingElseThePublisherThrows_IsTheFailureOfThatMessageAloneAndTheBatchGoesOn()
    {
        var disposed = Envelope(1);
        var fine = Envelope(2);
        QueueReturns(disposed, fine);
        Publisher.PublishAsync(Arg.Is<OutboxEnvelope>(candidate => candidate.Id == disposed.Id), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ObjectDisposedException("channel"));

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.Claimed.ShouldBe(2);
        outcome.Confirmed.ShouldBe(1);
        Telemetry.Failures.ShouldBe([PublishFailureReason.BrokerUnavailable]);
        await Queue.Received(1).ReleaseAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, disposed)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TwoHundredPublicationsThatAllFailWithAnUnexpectedException_AreAllCountedAndNoneEscapes()
    {
        var batch = Enumerable.Range(1, 200).Select(number => Envelope(number, minutesAfterBase: number)).ToArray();
        QueueReturns(batch);
        Publisher.PublishAsync(Arg.Any<OutboxEnvelope>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the channel was torn down"));

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.Claimed.ShouldBe(200);
        outcome.Confirmed.ShouldBe(0);
        Telemetry.Failures.Count.ShouldBe(200);
        await Queue.Received(1).ReleaseAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 200),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NotConnectedAndTheConnectionFails_ReturnsNotClaimedWithoutAskingForABudget()
    {
        Publisher.IsConnected.Returns(false);
        Publisher.TryConnectAsync(Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.WasClaimed.ShouldBeFalse();
        Publisher.DidNotReceive().ClaimBudget(Arg.Any<int>());
        await Queue.DidNotReceive().ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NotConnectedButTheConnectionSucceeds_ClaimsTheBatch()
    {
        Publisher.IsConnected.Returns(false);
        Publisher.TryConnectAsync(Arg.Any<CancellationToken>()).Returns(true);
        QueueReturns(Envelope(1));

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.WasClaimed.ShouldBeTrue();
        await Publisher.Received(1).TryConnectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ConnectedPublisher_DoesNotTryToConnectAgain()
    {
        QueueReturns(Envelope(1));

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        await Publisher.DidNotReceive().TryConnectAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BudgetOfOne_ClaimsExactlyOneMessageWithTheConfiguredLease()
    {
        Publisher.ClaimBudget(Arg.Any<int>()).Returns(1);
        QueueReturns(Envelope(1));

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        Publisher.Received(1).ClaimBudget(200);
        await Queue.Received(1).ClaimBatchAsync(1, TimeSpan.FromSeconds(30), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UnorderedClaim_PublishesTheMessagesOrderedByCreationInstant()
    {
        var third = Envelope(3, minutesAfterBase: 3);
        var first = Envelope(1, minutesAfterBase: 1);
        var second = Envelope(2, minutesAfterBase: 2);
        QueueReturns(third, first, second);

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        var published = Publisher.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IEventPublisher.PublishAsync))
            .Select(call => ((OutboxEnvelope)call.GetArguments()[0]!).Id)
            .ToList();
        published.ShouldBe([first.Id, second.Id, third.Id]);
    }

    [Fact]
    public async Task HandleAsync_AllConfirmed_MarksThemInASingleCallAndCountsThemPublished()
    {
        var batch = new[] { Envelope(1), Envelope(2), Envelope(3) };
        QueueReturns(batch);

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.Claimed.ShouldBe(3);
        outcome.Confirmed.ShouldBe(3);
        await Queue.Received(1).MarkPublishedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, batch)),
            Arg.Any<CancellationToken>());
        Telemetry.Published.ShouldBe([3]);
        Telemetry.Failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_PartialConfirmation_MarksOnlyTheConfirmedOnes()
    {
        var batch = Enumerable.Range(1, 6).Select(number => Envelope(number)).ToArray();
        QueueReturns(batch);
        PublishFails(batch[1], PublishFailureReason.Nack);
        PublishFails(batch[4], PublishFailureReason.Nack);

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.Confirmed.ShouldBe(4);
        await Queue.Received(1).MarkPublishedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, batch[0], batch[2], batch[3], batch[5])),
            Arg.Any<CancellationToken>());
        Telemetry.Published.ShouldBe([4]);
        Telemetry.Failures.ShouldBe([PublishFailureReason.Nack, PublishFailureReason.Nack]);
    }

    [Theory]
    [InlineData(PublishFailureReason.Nack)]
    [InlineData(PublishFailureReason.Timeout)]
    [InlineData(PublishFailureReason.BrokerUnavailable)]
    [InlineData(PublishFailureReason.Serialization)]
    [InlineData(PublishFailureReason.Unroutable)]
    public async Task HandleAsync_FailureOfAnyReason_IsCountedByReasonAndNeverRethrown(PublishFailureReason reason)
    {
        var batch = new[] { Envelope(1), Envelope(2) };
        QueueReturns(batch);
        PublishFails(batch[0], reason);

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.Confirmed.ShouldBe(1);
        Telemetry.Failures.ShouldBe([reason]);
        Telemetry.Publishes.Single(record => record.MessageId == batch[0].Id).Failure.ShouldBe(reason);
        Telemetry.Publishes.Single(record => record.MessageId == batch[1].Id).WasConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_SerializationFailureInTheMiddle_DoesNotStopTheOtherMessages()
    {
        var batch = Enumerable.Range(1, 5).Select(number => Envelope(number)).ToArray();
        QueueReturns(batch);
        PublishFails(batch[2], PublishFailureReason.Serialization);

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        await Queue.Received(1).MarkPublishedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, batch[0], batch[1], batch[3], batch[4])),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NothingConfirmed_DoesNotMarkAnythingNorCountPublished()
    {
        var batch = new[] { Envelope(1), Envelope(2) };
        QueueReturns(batch);
        PublishFails(batch[0], PublishFailureReason.BrokerUnavailable);
        PublishFails(batch[1], PublishFailureReason.BrokerUnavailable);

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.Confirmed.ShouldBe(0);
        await Queue.DidNotReceive().MarkPublishedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        Telemetry.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_EmptyClaim_ReturnsCompletedWithZeros()
    {
        QueueReturns();

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.WasClaimed.ShouldBeTrue();
        outcome.Claimed.ShouldBe(0);
        outcome.Confirmed.ShouldBe(0);
        Telemetry.Polls.Single().Size.ShouldBe(0);
    }

    [Fact]
    public async Task HandleAsync_SharedConfirmationDeadline_TurnsThePendingMessagesIntoTimeouts()
    {
        var batch = new[] { Envelope(1), Envelope(2), Envelope(3) };
        QueueReturns(batch);
        Publisher.PublishAsync(Arg.Is<OutboxEnvelope>(candidate => candidate.Id != batch[0].Id), Arg.Any<CancellationToken>())
            .Returns(call => Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>()));

        var running = Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);
        Time.Advance(TimeSpan.FromSeconds(5));
        var outcome = await running;

        outcome.Confirmed.ShouldBe(1);
        Telemetry.Failures.ShouldBe([PublishFailureReason.Timeout, PublishFailureReason.Timeout]);
        await Queue.Received(1).MarkPublishedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, batch[0])),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DeadlineIsNotReachedYet_LeavesThePendingMessagesWaiting()
    {
        var batch = new[] { Envelope(1) };
        QueueReturns(batch);
        var release = new TaskCompletionSource();
        Publisher.PublishAsync(Arg.Any<OutboxEnvelope>(), Arg.Any<CancellationToken>()).Returns(release.Task);

        var running = Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);
        Time.Advance(TimeSpan.FromSeconds(4));
        running.IsCompleted.ShouldBeFalse();
        release.SetResult();
        var outcome = await running;

        outcome.Confirmed.ShouldBe(1);
        Telemetry.Failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ServiceCancelledAfterTheClaim_StillMarksWhatWasConfirmedWithATokenOfItsOwn()
    {
        using var service = new CancellationTokenSource();
        var batch = new[] { Envelope(1), Envelope(2) };
        QueueReturns(batch);
        Publisher.PublishAsync(Arg.Is<OutboxEnvelope>(candidate => candidate.Id == batch[0].Id), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        Publisher.PublishAsync(Arg.Is<OutboxEnvelope>(candidate => candidate.Id == batch[1].Id), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await service.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
            });
        var markTokenWasCancelled = true;
        var markToken = service.Token;
        Queue.MarkPublishedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                markToken = call.Arg<CancellationToken>();
                markTokenWasCancelled = markToken.IsCancellationRequested;

                return 1;
            });

        await Should.ThrowAsync<OperationCanceledException>(
            () => Handler.HandleAsync(new PublishOutboxBatchCommand(), service.Token));

        await Queue.Received(1).MarkPublishedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, batch[0])),
            Arg.Any<CancellationToken>());
        service.IsCancellationRequested.ShouldBeTrue();
        markTokenWasCancelled.ShouldBeFalse();
        markToken.ShouldNotBe(service.Token);
        Telemetry.Failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_MarkingNeverReturns_GivesUpWhenTheLeaseRunsOut()
    {
        QueueReturns(Envelope(1));
        Queue.MarkPublishedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());

                return 0;
            });

        var running = Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);
        Time.Advance(TimeSpan.FromSeconds(29));
        running.IsCompleted.ShouldBeFalse();
        Time.Advance(TimeSpan.FromSeconds(1));

        await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(10)));
        Telemetry.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_QueueFailsToClaim_TheExceptionPropagates()
    {
        Queue.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(
            () => Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None));

        await Publisher.DidNotReceive().PublishAsync(Arg.Any<OutboxEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_QueueFailsToMark_TheExceptionPropagates()
    {
        QueueReturns(Envelope(1));
        Queue.MarkPublishedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(
            () => Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_MessageWithAttemptsAtTheThreshold_LogsStuckOncePerClaim()
    {
        var stuck = Envelope(1, attempts: 5);
        var alsoStuck = Envelope(2, attempts: 9);
        var fine = Envelope(3, attempts: 4);
        QueueReturns(stuck, alsoStuck, fine);

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        var logs = Logger.Entries.Where(entry => entry.EventId.Id == 3005).ToList();
        logs.Count.ShouldBe(2);
        logs.ShouldAllBe(entry => entry.Level == LogLevel.Warning);
        logs.Select(entry => entry.Properties["MessageId"]).Order()
            .ShouldBe(new[] { stuck.Id.ToString(), alsoStuck.Id.ToString() }.Order());
        logs.Single(entry => entry.Properties["MessageId"] == alsoStuck.Id.ToString()).Properties["Attempts"].ShouldBe("9");
    }

    [Fact]
    public async Task HandleAsync_FailedPublication_LogsAWarningWithReasonMessageAndAttempts()
    {
        var failing = Envelope(1, attempts: 3);
        QueueReturns(failing);
        PublishFails(failing, PublishFailureReason.Timeout);

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        var log = Logger.Single(3002);
        log.Level.ShouldBe(LogLevel.Warning);
        log.Properties["Reason"].ShouldBe("timeout");
        log.Properties["MessageId"].ShouldBe(failing.Id.ToString());
        log.Properties["Attempts"].ShouldBe("3");
    }

    [Fact]
    public async Task HandleAsync_BatchPublished_LogsAtDebugWithoutTheBodyOfAnyMessage()
    {
        QueueReturns(Envelope(1), Envelope(2));

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        var log = Logger.Single(3001);
        log.Level.ShouldBe(LogLevel.Debug);
        log.Properties["Claimed"].ShouldBe("2");
        log.Properties["Published"].ShouldBe("2");
        foreach (var entry in Logger.Entries)
        {
            entry.Message.ShouldNotContain("secret-value");
            entry.Properties.Values.ShouldNotContain("{\"payload\":\"secret-value\"}");
        }
    }

    [Fact]
    public async Task HandleAsync_EmptyBatch_LogsAtTraceSoTheIdlePollNeverMakesNoise()
    {
        QueueReturns();

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.WasClaimed.ShouldBeTrue();
        outcome.Claimed.ShouldBe(0);

        var log = Logger.Single(3001);

        log.Level.ShouldBe(LogLevel.Trace);
        log.Properties["Claimed"].ShouldBe("0");
        log.Properties["Published"].ShouldBe("0");
    }

    [Fact]
    public async Task HandleAsync_UnroutableMessages_AreReportedOncePerBatchAndNeverAsOnePublishFailureEach()
    {
        var batch = Enumerable.Range(1, 200).Select(number => Envelope(number, minutesAfterBase: number)).ToArray();
        QueueReturns(batch);
        Publisher.PublishAsync(Arg.Any<OutboxEnvelope>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new EventPublishException(PublishFailureReason.Unroutable, Guid.Empty));

        var outcome = await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.Claimed.ShouldBe(200);
        outcome.Confirmed.ShouldBe(0);
        Telemetry.Failures.Count.ShouldBe(200);
        Telemetry.Failures.ShouldAllBe(reason => reason == PublishFailureReason.Unroutable);
        Logger.Entries.Count(entry => entry.EventId.Id == 3002).ShouldBe(0);

        var report = Logger.Single(3009);

        report.Level.ShouldBe(LogLevel.Warning);
        report.Properties["Unroutable"].ShouldBe("200");
        await Queue.Received(1).ReleaseAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 200),
            Arg.Any<CancellationToken>());
        await Queue.DidNotReceive().MarkPublishedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BatchWithoutUnroutableMessages_NeverLogsTheUnroutableReport()
    {
        QueueReturns(Envelope(1), Envelope(2));

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        Logger.Contains(3009).ShouldBeFalse();
    }

    [Fact]
    public async Task HandleAsync_OpensOnePollSpanAndOnePublishSpanPerMessageAndDisposesThemAll()
    {
        var batch = new[] { Envelope(1), Envelope(2), Envelope(3) };
        QueueReturns(batch);
        PublishFails(batch[1], PublishFailureReason.Nack);

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        var poll = Telemetry.Polls.Single();
        poll.Size.ShouldBe(3);
        poll.Disposed.ShouldBeTrue();
        Telemetry.Publishes.Count.ShouldBe(3);
        Telemetry.Publishes.ShouldAllBe(record => record.Disposed);
        Telemetry.Publishes.Count(record => record.WasConfirmed).ShouldBe(2);
        Telemetry.Publishes.Single(record => record.Failure == PublishFailureReason.Nack).MessageId.ShouldBe(batch[1].Id);
    }

    [Fact]
    public async Task HandleAsync_ServiceToken_ReachesTheQueueAndCancelsThePublisherToken()
    {
        QueueReturns(Envelope(1));
        using var source = new CancellationTokenSource();
        var publisherTokenFollowedTheService = false;
        Publisher.PublishAsync(Arg.Any<OutboxEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var publisherToken = call.Arg<CancellationToken>();
                publisherTokenFollowedTheService = !publisherToken.IsCancellationRequested;
                await source.CancelAsync();
                publisherTokenFollowedTheService &= publisherToken.IsCancellationRequested;
            });

        await Handler.HandleAsync(new PublishOutboxBatchCommand(), source.Token);

        await Queue.Received(1).ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), source.Token);
        publisherTokenFollowedTheService.ShouldBeTrue();
    }

    [Fact]
    public void ToString_OfAnEnvelope_NeverPrintsThePayload()
    {
        Envelope(1).ToString().ShouldNotContain("secret-value");
    }
}
