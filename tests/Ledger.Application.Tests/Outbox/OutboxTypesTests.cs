using Ledger.Application.Outbox;

namespace Ledger.Application.Tests.Outbox;

[Trait("Category", "Unit")]
public sealed class OutboxTypesTests
{
    private static OutboxSettings Settings(
        int batchSize = 200,
        int leaseSeconds = 30,
        int confirmSeconds = 5,
        int pruneBatch = 5000) =>
        new(
            batchSize,
            TimeSpan.FromSeconds(leaseSeconds),
            TimeSpan.FromSeconds(confirmSeconds),
            5,
            1000,
            1_000_000,
            TimeSpan.FromDays(7),
            pruneBatch);

    [Fact]
    public void BrokerCircuitState_HasTheNumericValuesOfTheMetric()
    {
        ((int)BrokerCircuitState.Closed).ShouldBe(0);
        ((int)BrokerCircuitState.HalfOpen).ShouldBe(1);
        ((int)BrokerCircuitState.Open).ShouldBe(2);
    }

    [Theory]
    [InlineData(PublishFailureReason.BrokerUnavailable, "broker_unavailable")]
    [InlineData(PublishFailureReason.Nack, "nack")]
    [InlineData(PublishFailureReason.Timeout, "timeout")]
    [InlineData(PublishFailureReason.Serialization, "serialization")]
    [InlineData(PublishFailureReason.Unroutable, "unroutable")]
    public void PublishFailureReason_MapsToTheMetricLabel(PublishFailureReason reason, string label)
    {
        reason.ToLabel().ShouldBe(label);
    }

    [Fact]
    public void PublishFailureReason_UnknownValue_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ((PublishFailureReason)99).ToLabel());
    }

    [Fact]
    public void EventPublishException_CarriesTheReasonAndTheMessageIdButNeverTheBody()
    {
        var messageId = Guid.Parse("0192b7c4-5d12-7c88-a0e4-9d3b6f1a2c75");

        var exception = new EventPublishException(PublishFailureReason.Nack, messageId);

        exception.Reason.ShouldBe(PublishFailureReason.Nack);
        exception.MessageId.ShouldBe(messageId);
        exception.Message.ShouldContain("nack");
        exception.Message.ShouldNotContain(messageId.ToString());
    }

    [Fact]
    public void EventPublishException_KeepsTheInnerException()
    {
        var inner = new IOException("socket closed");

        var exception = new EventPublishException(PublishFailureReason.BrokerUnavailable, Guid.NewGuid(), inner);

        exception.InnerException.ShouldBe(inner);
    }

    [Fact]
    public void OutboxSettings_ValidValues_AreKept()
    {
        var settings = Settings();

        settings.BatchSize.ShouldBe(200);
        settings.Lease.ShouldBe(TimeSpan.FromSeconds(30));
        settings.ConfirmTimeout.ShouldBe(TimeSpan.FromSeconds(5));
        settings.FailedAttempts.ShouldBe(5);
        settings.FailedHeadWindow.ShouldBe(1000);
        settings.PendingCap.ShouldBe(1_000_000);
        settings.Retention.ShouldBe(TimeSpan.FromDays(7));
        settings.PruneBatchSize.ShouldBe(5000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OutboxSettings_BatchSizeBelowOne_Throws(int batchSize)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Settings(batchSize: batchSize));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(4, 5)]
    public void OutboxSettings_LeaseNotGreaterThanTheConfirmationTimeout_Throws(int leaseSeconds, int confirmSeconds)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Settings(leaseSeconds: leaseSeconds, confirmSeconds: confirmSeconds));
    }

    [Fact]
    public void OutboxSettings_PruneBatchBelowOne_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Settings(pruneBatch: 0));
    }

    [Fact]
    public void WorkerLoop_HasOneEntryPerLoopOfTheWorker()
    {
        Enum.GetNames<WorkerLoop>().Order().ShouldBe(
        [
            "IdempotencyPrune",
            "IntegrityFull",
            "IntegrityRecent",
            "KeyRewrap",
            "Measure",
            "Outbox",
            "OutboxPrune"
        ]);
    }

    [Theory]
    [InlineData(WorkerLoop.Outbox, "outbox")]
    [InlineData(WorkerLoop.IntegrityRecent, "integrity-recent")]
    [InlineData(WorkerLoop.IntegrityFull, "integrity-full")]
    [InlineData(WorkerLoop.Measure, "measure")]
    [InlineData(WorkerLoop.OutboxPrune, "prune")]
    [InlineData(WorkerLoop.IdempotencyPrune, "idempotency-prune")]
    [InlineData(WorkerLoop.KeyRewrap, "rewrap")]
    public void WorkerLoop_NameIsTheOneTheLogsAndMetricsUse(WorkerLoop loop, string expected)
    {
        loop.Name().ShouldBe(expected);
    }

    [Fact]
    public void Outcome_NotClaimed_ReportsTheCircuitAndNothingClaimed()
    {
        var outcome = PublishOutboxBatchOutcome.NotClaimed(BrokerCircuitState.Open);

        outcome.WasClaimed.ShouldBeFalse();
        outcome.Claimed.ShouldBe(0);
        outcome.Confirmed.ShouldBe(0);
        outcome.Circuit.ShouldBe(BrokerCircuitState.Open);
    }

    [Fact]
    public void Outcome_Completed_ReportsTheCounts()
    {
        var outcome = PublishOutboxBatchOutcome.Completed(20, 15, BrokerCircuitState.Closed);

        outcome.WasClaimed.ShouldBeTrue();
        outcome.Claimed.ShouldBe(20);
        outcome.Confirmed.ShouldBe(15);
    }
}
