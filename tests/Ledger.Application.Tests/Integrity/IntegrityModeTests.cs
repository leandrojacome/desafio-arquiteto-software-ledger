using Ledger.Application.Integrity;

namespace Ledger.Application.Tests.Integrity;

[Trait("Category", "Unit")]
public sealed class IntegrityModeTests
{
    [Fact]
    public void Recent_HasTheAuditTextTheMetricLabelAndTheLockKey()
    {
        IntegrityMode.Recent.AuditText().ShouldBe("RECENT");
        IntegrityMode.Recent.MetricLabel().ShouldBe("incremental");
        IntegrityMode.Recent.LockKey().ShouldBe(1);
    }

    [Fact]
    public void Full_HasTheAuditTextTheMetricLabelAndTheLockKey()
    {
        IntegrityMode.Full.AuditText().ShouldBe("FULL");
        IntegrityMode.Full.MetricLabel().ShouldBe("full");
        IntegrityMode.Full.LockKey().ShouldBe(2);
    }

    [Fact]
    public void UnknownMode_Throws()
    {
        const IntegrityMode unknown = 0;

        Should.Throw<ArgumentOutOfRangeException>(() => unknown.AuditText());
        Should.Throw<ArgumentOutOfRangeException>(() => unknown.MetricLabel());
        Should.Throw<ArgumentOutOfRangeException>(() => unknown.LockKey());
    }

    [Theory]
    [InlineData(IntegrityCheck.HeadBalance, "HEAD_BALANCE", "balance_mismatch")]
    [InlineData(IntegrityCheck.HeadVersion, "HEAD_VERSION", "balance_mismatch")]
    [InlineData(IntegrityCheck.HeadLastEntry, "HEAD_LAST_ENTRY", "balance_mismatch")]
    [InlineData(IntegrityCheck.HeadFloor, "HEAD_FLOOR", "balance_mismatch")]
    [InlineData(IntegrityCheck.SumBalance, "SUM_BALANCE", "balance_mismatch")]
    [InlineData(IntegrityCheck.ChainDrift, "CHAIN_DRIFT", "chain_broken")]
    [InlineData(IntegrityCheck.ChainGap, "CHAIN_GAP", "chain_broken")]
    [InlineData(IntegrityCheck.ChainNonMonotonic, "CHAIN_NON_MONOTONIC", "chain_broken")]
    public void Check_HasTheAuditNameAndTheMetricKind(IntegrityCheck check, string name, string kind)
    {
        check.AuditName().ShouldBe(name);
        check.MetricKind().ShouldBe(kind);
    }

    [Fact]
    public void UnknownCheck_Throws()
    {
        const IntegrityCheck unknown = 0;

        Should.Throw<ArgumentOutOfRangeException>(() => unknown.AuditName());
        Should.Throw<ArgumentOutOfRangeException>(() => unknown.MetricKind());
    }
}
