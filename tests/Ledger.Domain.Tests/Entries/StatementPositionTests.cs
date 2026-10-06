using Ledger.Domain.Entries;

namespace Ledger.Domain.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class StatementPositionTests
{
    private const long OneMicrosecondInTicks = 10;

    private static readonly DateTimeOffset Instant = new(2026, 10, 1, 14, 3, 11, TimeSpan.Zero);

    [Fact]
    public void CompareTo_OrdersByTheInstantFirst()
    {
        var earlier = new StatementPosition(Instant, 900);
        var later = new StatementPosition(Instant.AddTicks(OneMicrosecondInTicks), 1);

        earlier.CompareTo(later).ShouldBeLessThan(0);
        later.CompareTo(earlier).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void CompareTo_OnTheSameInstant_OrdersByTheAccountVersion()
    {
        var first = new StatementPosition(Instant, 1);
        var second = new StatementPosition(Instant, 2);

        first.CompareTo(second).ShouldBeLessThan(0);
        second.CompareTo(first).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void CompareTo_ForEqualPositions_IsZero()
    {
        var first = new StatementPosition(Instant, 7);
        var second = new StatementPosition(Instant, 7);

        first.CompareTo(second).ShouldBe(0);
    }

    [Fact]
    public void CompareTo_ComparesInstantsAndNotOffsets()
    {
        var utc = new StatementPosition(Instant, 5);
        var local = new StatementPosition(Instant.ToOffset(TimeSpan.FromHours(-3)), 5);

        utc.CompareTo(local).ShouldBe(0);
        utc.ShouldBe(local);
    }

    [Fact]
    public void Before_HasTheSmallestPossibleAccountVersion()
    {
        var position = StatementPosition.Before(Instant);

        position.RecordedAt.ShouldBe(Instant);
        position.AccountVersion.ShouldBe(long.MinValue);
    }

    [Fact]
    public void Before_IsLowerThanEveryRealPositionOfTheSameInstant()
    {
        var before = StatementPosition.Before(Instant);

        (before < new StatementPosition(Instant, 1)).ShouldBeTrue();
        (before < new StatementPosition(Instant, long.MaxValue)).ShouldBeTrue();
        (before < new StatementPosition(Instant, 0)).ShouldBeTrue();
    }

    [Fact]
    public void Before_IsGreaterThanEveryPositionOneMicrosecondEarlier()
    {
        var before = StatementPosition.Before(Instant);
        var earlier = new StatementPosition(Instant.AddTicks(-OneMicrosecondInTicks), long.MaxValue);

        (before > earlier).ShouldBeTrue();
        before.CompareTo(earlier).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        var first = new StatementPosition(Instant, 10);
        var second = new StatementPosition(Instant, 10);
        var different = new StatementPosition(Instant, 11);

        (first == second).ShouldBeTrue();
        (first != different).ShouldBeTrue();
        first.Equals(second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Operators_AgreeWithCompareTo()
    {
        var low = new StatementPosition(Instant, 1);
        var high = new StatementPosition(Instant, 2);

        (low < high).ShouldBeTrue();
        (low <= high).ShouldBeTrue();
        (high > low).ShouldBeTrue();
        (high >= low).ShouldBeTrue();
        (low >= high).ShouldBeFalse();
        (low > high).ShouldBeFalse();
        (high <= low).ShouldBeFalse();
        (high < low).ShouldBeFalse();
        (low <= new StatementPosition(Instant, 1)).ShouldBeTrue();
        (low >= new StatementPosition(Instant, 1)).ShouldBeTrue();
        (low < new StatementPosition(Instant, 1)).ShouldBeFalse();
        (low > new StatementPosition(Instant, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Constructor_PreservesMicrosecondPrecision()
    {
        var precise = new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero).AddTicks(4829130);

        var position = new StatementPosition(precise, 1843);

        position.RecordedAt.Ticks.ShouldBe(precise.Ticks);
        position.AccountVersion.ShouldBe(1843);
    }

    [Fact]
    public void Sorting_AListOfPositions_PutsTheNewestLast()
    {
        var positions = new List<StatementPosition>
        {
            new(Instant.AddSeconds(1), 3),
            new(Instant, 2),
            new(Instant, 1),
            new(Instant.AddSeconds(-1), 9)
        };

        positions.Sort();

        positions.Select(position => position.AccountVersion).ShouldBe([9L, 1L, 2L, 3L]);
    }

    [Fact]
    public void Max_AndMin_AreAvailableThroughTheSameOrdering()
    {
        var low = new StatementPosition(Instant, 1);
        var high = new StatementPosition(Instant.AddTicks(1), 0);

        new[] { high, low }.Min().ShouldBe(low);
        new[] { low, high }.Max().ShouldBe(high);
    }
}
