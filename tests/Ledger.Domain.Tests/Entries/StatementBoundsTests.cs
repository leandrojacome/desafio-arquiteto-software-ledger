using Ledger.Domain.Entries;

namespace Ledger.Domain.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class StatementBoundsTests
{
    private const long OneMicrosecondInTicks = 10;

    private static readonly DateTimeOffset From = new(2026, 10, 1, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 10, 2, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resolve_PassesFromThrough()
    {
        var bounds = StatementBounds.Resolve(From, null, null);

        bounds.From.ShouldBe(From);
    }

    [Fact]
    public void Resolve_WithoutFrom_KeepsItAbsent()
    {
        var bounds = StatementBounds.Resolve(null, To, null);

        bounds.From.ShouldBeNull();
    }

    [Fact]
    public void Resolve_WithoutToAndWithoutCursor_HasNoUpperBound()
    {
        var bounds = StatementBounds.Resolve(From, null, null);

        bounds.Upper.ShouldBeNull();
    }

    [Fact]
    public void Resolve_WithOnlyTo_IsThePositionRightBeforeEveryEntryOfThatInstant()
    {
        var bounds = StatementBounds.Resolve(null, To, null);

        bounds.Upper.ShouldBe(new StatementPosition(To, long.MinValue));
    }

    [Fact]
    public void Resolve_WithOnlyTheCursor_IsTheCursor()
    {
        var cursor = new StatementPosition(To.AddHours(-5), 1843);

        var bounds = StatementBounds.Resolve(From, null, cursor);

        bounds.Upper.ShouldBe(cursor);
    }

    [Fact]
    public void Resolve_WhenToIsBeforeTheCursorInstant_UsesTo()
    {
        var cursor = new StatementPosition(To.AddMinutes(10), 1843);

        var bounds = StatementBounds.Resolve(From, To, cursor);

        bounds.Upper.ShouldBe(new StatementPosition(To, long.MinValue));
    }

    [Fact]
    public void Resolve_WhenToEqualsTheCursorInstant_UsesTheToPositionBecauseItIsLowerThanTheCursor()
    {
        var cursor = new StatementPosition(To, 1843);

        var bounds = StatementBounds.Resolve(From, To, cursor);

        bounds.Upper.ShouldBe(new StatementPosition(To, long.MinValue));
        (bounds.Upper.GetValueOrDefault() < cursor).ShouldBeTrue();
    }

    [Fact]
    public void Resolve_WhenToIsOneMicrosecondAfterTheCursorInstant_UsesTheCursor()
    {
        var cursor = new StatementPosition(To.AddTicks(-OneMicrosecondInTicks), 1843);

        var bounds = StatementBounds.Resolve(From, To, cursor);

        bounds.Upper.ShouldBe(cursor);
    }

    [Fact]
    public void Resolve_WhenToIsFarAfterTheCursor_UsesTheCursor()
    {
        var cursor = new StatementPosition(To.AddDays(-30), 12);

        var bounds = StatementBounds.Resolve(null, To, cursor);

        bounds.Upper.ShouldBe(cursor);
    }

    [Fact]
    public void Resolve_WithACursorAtTheMinimumVersionOnTheToInstant_StillReturnsAnEqualPosition()
    {
        var cursor = new StatementPosition(To, long.MinValue);

        var bounds = StatementBounds.Resolve(null, To, cursor);

        bounds.Upper.ShouldBe(cursor);
    }

    [Fact]
    public void Resolve_ComparesInstantsAcrossOffsets()
    {
        var cursor = new StatementPosition(To.ToOffset(TimeSpan.FromHours(-3)), 77);

        var bounds = StatementBounds.Resolve(null, To, cursor);

        bounds.Upper.ShouldBe(new StatementPosition(To, long.MinValue));
    }

    [Fact]
    public void Resolve_IsPure_TwoCallsWithTheSameInputGiveEqualResults()
    {
        var cursor = new StatementPosition(To.AddHours(-1), 5);

        var first = StatementBounds.Resolve(From, To, cursor);
        var second = StatementBounds.Resolve(From, To, cursor);

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Resolve_DoesNotValidateTheOrderOfFromAndTo()
    {
        var bounds = StatementBounds.Resolve(To, From, null);

        bounds.From.ShouldBe(To);
        bounds.Upper.ShouldBe(new StatementPosition(From, long.MinValue));
    }

    [Fact]
    public void Resolve_KeepsMicrosecondPrecisionOfTo()
    {
        var precise = To.AddTicks(4829130);

        var bounds = StatementBounds.Resolve(null, precise, null);

        bounds.Upper.GetValueOrDefault().RecordedAt.Ticks.ShouldBe(precise.Ticks);
    }

    [Fact]
    public void StatementBounds_IsSealed()
    {
        typeof(StatementBounds).IsSealed.ShouldBeTrue();
    }

    [Fact]
    public void StatementBounds_HasNoPublicConstructor()
    {
        typeof(StatementBounds)
            .GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_WithFromAndToInTheBrasiliaOffset_KeepsBothInUtc()
    {
        var fromInBrasilia = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-3));
        var toInBrasilia = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(-3));

        var bounds = StatementBounds.Resolve(fromInBrasilia, toInBrasilia, null);

        bounds.From.ShouldBe(From);
        bounds.From.ShouldNotBeNull().Offset.ShouldBe(TimeSpan.Zero);
        bounds.Upper.ShouldNotBeNull().RecordedAt.ShouldBe(To);
        bounds.Upper.ShouldNotBeNull().RecordedAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Resolve_WithToInAnotherOffsetAndACursorAhead_UsesToInUtc()
    {
        var toInBrasilia = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(-3));
        var cursor = new StatementPosition(To.AddMinutes(10), 1843);

        var bounds = StatementBounds.Resolve(null, toInBrasilia, cursor);

        bounds.Upper.ShouldBe(new StatementPosition(To, long.MinValue));
        bounds.Upper.ShouldNotBeNull().RecordedAt.Offset.ShouldBe(TimeSpan.Zero);
    }
}
