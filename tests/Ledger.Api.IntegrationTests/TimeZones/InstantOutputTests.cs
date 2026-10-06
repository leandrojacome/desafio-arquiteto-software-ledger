using Ledger.Api.Contracts;

namespace Ledger.Api.IntegrationTests.TimeZones;

[Trait("Category", "Unit")]
public sealed class InstantOutputTests
{
    [Fact]
    public void From_AnInstantWithTheBrasiliaOffset_IsWrittenInUtcWithZ()
    {
        var local = new DateTimeOffset(2026, 10, 1, 11, 3, 10, TimeSpan.FromHours(-3));

        InstantText.From(local).ShouldBe("2026-10-01T14:03:10.000000Z");
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(14)]
    [InlineData(-14)]
    public void From_TheSameInstantInAnyOffset_IsWrittenTheSameWay(int hours)
    {
        var utc = new DateTimeOffset(2026, 10, 1, 14, 3, 10, TimeSpan.Zero).AddTicks(1_234_560);
        var local = utc.ToOffset(TimeSpan.FromHours(hours));

        InstantText.From(local).ShouldBe("2026-10-01T14:03:10.123456Z");
        InstantText.From(local).ShouldBe(InstantText.From(utc));
    }

    [Fact]
    public void From_AFractionalOffset_IsWrittenInUtcWithZ()
    {
        var local = new DateTimeOffset(2026, 10, 1, 19, 33, 10, TimeSpan.FromMinutes(330));

        InstantText.From(local).ShouldBe("2026-10-01T14:03:10.000000Z");
    }

    [Fact]
    public void From_AnInstantNearMidnightOfBrasilia_MovesTheDateInUtc()
    {
        var local = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.FromHours(-3));

        InstantText.From(local).ShouldBe("2027-01-01T02:30:00.000000Z");
    }

    [Fact]
    public void From_NeverWritesAnOffsetOtherThanZ()
    {
        var text = InstantText.From(new DateTimeOffset(2026, 10, 1, 11, 3, 10, TimeSpan.FromHours(-3)));

        text.ShouldEndWith("Z");
        text.ShouldNotContain("-03:00");
        text.ShouldNotContain("+");
    }
}
