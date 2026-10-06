using Ledger.Application.Integrity;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Tests.Integrity;

[Trait("Category", "Unit")]
public sealed class IntegrityClassifierTests
{
    private static readonly AccountId Account = AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;
    private static readonly EntryId Latest = EntryId.From(Guid.Parse("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10")).Value;
    private static readonly EntryId Other = EntryId.From(Guid.Parse("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f11")).Value;
    private static readonly DateTimeOffset Instant = new DateTimeOffset(2026, 10, 1, 22, 0, 0, TimeSpan.Zero).AddTicks(4_829_130);

    private static HeadRow Head(
        decimal balance = 920.00m,
        decimal limit = 0m,
        long version = 1843,
        EntryId? lastEntry = null,
        decimal? latestBalance = 920.00m,
        long? latestVersion = 1843,
        EntryId? latestEntry = null) =>
        new(
            Account,
            balance,
            limit,
            version,
            lastEntry ?? Latest,
            latestBalance,
            latestVersion,
            latestEntry ?? Latest);

    private static ChainRow Chain(
        decimal drift = 0m,
        bool missing = false,
        bool nonMonotonic = false,
        decimal balanceAfter = 921.00m,
        long version = 1843,
        DateTimeOffset? previous = null) =>
        new(Account, version, Latest, balanceAfter, Instant, previous ?? Instant.AddSeconds(-1), drift, missing, nonMonotonic);

    [Fact]
    public void FromHead_ConsistentRow_ReturnsNothing()
    {
        IntegrityClassifier.FromHead(Head()).ShouldBeEmpty();
    }

    [Fact]
    public void FromHead_AccountWithoutEntries_ReturnsNothingWhenEverythingIsZeroAndNull()
    {
        var row = new HeadRow(Account, 0m, 0m, 0, null, null, null, null);

        IntegrityClassifier.FromHead(row).ShouldBeEmpty();
    }

    [Fact]
    public void FromHead_BalanceDifferentFromTheLastBalanceAfter_ReturnsHeadBalance()
    {
        var findings = IntegrityClassifier.FromHead(Head(balance: 921.00m));

        var finding = findings.ShouldHaveSingleItem();
        finding.Check.ShouldBe(IntegrityCheck.HeadBalance);
        finding.AccountId.ShouldBe(Account);
        finding.EntryId.ShouldBeNull();
        finding.AccountVersion.ShouldBeNull();
        finding.Expected.ShouldBe("920.00");
        finding.Found.ShouldBe("921.00");
    }

    [Fact]
    public void FromHead_NonZeroBalanceWithoutEntries_ComparesAgainstZero()
    {
        var row = new HeadRow(Account, 5m, 0m, 0, null, null, null, null);

        var finding = IntegrityClassifier.FromHead(row).ShouldHaveSingleItem();

        finding.Check.ShouldBe(IntegrityCheck.HeadBalance);
        finding.Expected.ShouldBe("0.00");
        finding.Found.ShouldBe("5.00");
    }

    [Fact]
    public void FromHead_VersionDifferentFromTheLastAccountVersion_ReturnsHeadVersion()
    {
        var finding = IntegrityClassifier.FromHead(Head(version: 1844)).ShouldHaveSingleItem();

        finding.Check.ShouldBe(IntegrityCheck.HeadVersion);
        finding.Expected.ShouldBe("1843");
        finding.Found.ShouldBe("1844");
    }

    [Fact]
    public void FromHead_LastEntryIdDifferentFromTheLatestEntry_ReturnsHeadLastEntryWithLowercaseIds()
    {
        var finding = IntegrityClassifier.FromHead(Head(lastEntry: Other)).ShouldHaveSingleItem();

        finding.Check.ShouldBe(IntegrityCheck.HeadLastEntry);
        finding.Expected.ShouldBe("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10");
        finding.Found.ShouldBe("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f11");
    }

    [Fact]
    public void FromHead_StoredLastEntryButNoEntries_ReturnsHeadLastEntryWithNoneAsExpected()
    {
        var row = new HeadRow(Account, 0m, 0m, 0, Other, null, null, null);

        var finding = IntegrityClassifier.FromHead(row).ShouldHaveSingleItem();

        finding.Check.ShouldBe(IntegrityCheck.HeadLastEntry);
        finding.Expected.ShouldBe("none");
        finding.Found.ShouldBe("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f11");
    }

    [Fact]
    public void FromHead_BalanceBelowTheNegativeLimit_ReturnsHeadFloor()
    {
        var row = Head(balance: -50.01m, limit: 50.00m, latestBalance: -50.01m);

        var finding = IntegrityClassifier.FromHead(row).ShouldHaveSingleItem();

        finding.Check.ShouldBe(IntegrityCheck.HeadFloor);
        finding.Expected.ShouldBe("-50.00");
        finding.Found.ShouldBe("-50.01");
    }

    [Fact]
    public void FromHead_BalanceExactlyAtTheNegativeLimit_ReturnsNothing()
    {
        var row = Head(balance: -50.00m, limit: 50.00m, latestBalance: -50.00m);

        IntegrityClassifier.FromHead(row).ShouldBeEmpty();
    }

    [Fact]
    public void FromHead_ZeroLimit_WritesTheFloorWithoutANegativeSign()
    {
        var row = Head(balance: -1.00m, limit: 0m, latestBalance: -1.00m);

        var finding = IntegrityClassifier.FromHead(row).ShouldHaveSingleItem();

        finding.Expected.ShouldBe("0.00");
    }

    [Fact]
    public void FromHead_EveryConditionAtOnce_ReturnsFourFindingsInOrder()
    {
        var row = new HeadRow(Account, -10m, 5m, 7, Other, 3m, 3, Latest);

        var findings = IntegrityClassifier.FromHead(row);

        findings.Select(finding => finding.Check).ShouldBe(
        [
            IntegrityCheck.HeadBalance,
            IntegrityCheck.HeadVersion,
            IntegrityCheck.HeadLastEntry,
            IntegrityCheck.HeadFloor
        ]);
    }

    [Fact]
    public void FromChain_ConsistentRow_ReturnsNothing()
    {
        IntegrityClassifier.FromChain(Chain()).ShouldBeEmpty();
    }

    [Fact]
    public void FromChain_DriftOfOne_ReturnsChainDriftWithTheExpectedBalanceAfter()
    {
        var finding = IntegrityClassifier.FromChain(Chain(drift: 1.00m, balanceAfter: 921.00m)).ShouldHaveSingleItem();

        finding.Check.ShouldBe(IntegrityCheck.ChainDrift);
        finding.EntryId.ShouldBe(Latest);
        finding.AccountVersion.ShouldBe(1843);
        finding.Expected.ShouldBe("920.00");
        finding.Found.ShouldBe("921.00");
    }

    [Fact]
    public void FromChain_NegativeDrift_ReturnsChainDriftWithTheExpectedBalanceAfter()
    {
        var finding = IntegrityClassifier.FromChain(Chain(drift: -1.00m, balanceAfter: 920.00m)).ShouldHaveSingleItem();

        finding.Expected.ShouldBe("921.00");
        finding.Found.ShouldBe("920.00");
    }

    [Fact]
    public void FromChain_MissingPredecessor_ReturnsOnlyChainGapEvenWithANonZeroDrift()
    {
        var finding = IntegrityClassifier.FromChain(Chain(drift: 40m, missing: true, version: 4)).ShouldHaveSingleItem();

        finding.Check.ShouldBe(IntegrityCheck.ChainGap);
        finding.AccountVersion.ShouldBe(4);
        finding.Expected.ShouldBe("3");
        finding.Found.ShouldBe("missing");
    }

    [Fact]
    public void FromChain_InstantThatDoesNotAdvance_ReturnsChainNonMonotonic()
    {
        var previous = Instant.AddSeconds(5);

        var finding = IntegrityClassifier.FromChain(Chain(nonMonotonic: true, previous: previous)).ShouldHaveSingleItem();

        finding.Check.ShouldBe(IntegrityCheck.ChainNonMonotonic);
        finding.Expected.ShouldBe("after 2026-10-01T22:00:05.482913Z");
        finding.Found.ShouldBe("2026-10-01T22:00:00.482913Z");
    }

    [Fact]
    public void FromChain_DriftAndRegressionTogether_ReturnsTwoFindings()
    {
        var findings = IntegrityClassifier.FromChain(Chain(drift: 2m, nonMonotonic: true));

        findings.Select(finding => finding.Check).ShouldBe([IntegrityCheck.ChainDrift, IntegrityCheck.ChainNonMonotonic]);
    }

    [Fact]
    public void FromSum_EqualValues_ReturnsNothing()
    {
        IntegrityClassifier.FromSum(Account, 100.00m, 100.00m).ShouldBeEmpty();
    }

    [Fact]
    public void FromSum_StoredBalanceDifferentFromTheSum_ReturnsSumBalance()
    {
        var finding = IntegrityClassifier.FromSum(Account, 101.5m, 100m).ShouldHaveSingleItem();

        finding.Check.ShouldBe(IntegrityCheck.SumBalance);
        finding.EntryId.ShouldBeNull();
        finding.Expected.ShouldBe("100.00");
        finding.Found.ShouldBe("101.50");
    }

    [Fact]
    public void Findings_ToString_NeverPrintsTheExpectedOrFoundValues()
    {
        var finding = IntegrityClassifier.FromSum(Account, 12345.67m, 7.77m).Single();

        var text = finding.ToString();

        text.ShouldNotContain("12345.67");
        text.ShouldNotContain("7.77");
        text.ShouldContain(Account.ToString());
    }

    [Fact]
    public void Rows_ToString_NeverPrintsMoney()
    {
        Head(balance: 12345.67m).ToString().ShouldNotContain("12345.67");
        Chain(balanceAfter: 12345.67m).ToString().ShouldNotContain("12345.67");
    }
}
