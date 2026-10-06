using System.Globalization;
using System.Text.RegularExpressions;
using Ledger.Api.Contracts;
using Ledger.Api.Validation;

namespace Ledger.Api.IntegrationTests.TimeZones;

[Trait("Category", "Unit")]
public sealed partial class InstantReadingTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 1, 14, 3, 10, TimeSpan.Zero);

    public static TheoryData<string, long> AcceptedInstants => new()
    {
        { "2026-10-01T14:03:10Z", Base.UtcTicks },
        { "2026-10-01T14:03:10+00:00", Base.UtcTicks },
        { "2026-10-01T11:03:10-03:00", Base.UtcTicks },
        { "2026-10-01T17:03:10+03:00", Base.UtcTicks },
        { "2026-10-01T19:33:10+05:30", Base.UtcTicks },
        { "2026-10-01T10:03:10-04:00", Base.UtcTicks },
        { "2026-10-01T12:03:10-02:00", Base.UtcTicks },
        { "2026-10-01T09:03:10-05:00", Base.UtcTicks },
        { "2026-10-01T00:03:10-14:00", Base.UtcTicks },
        { "2026-10-02T04:03:10+14:00", Base.UtcTicks },
        { "2026-10-01T14:03:10+14:00", Base.UtcTicks - TimeSpan.FromHours(14).Ticks },
        { "2026-10-01T14:03:10-14:00", Base.UtcTicks + TimeSpan.FromHours(14).Ticks },
        { "2026-10-01T11:03:10.5-03:00", Base.UtcTicks + 5_000_000 },
        { "2026-10-01T11:03:10.123456-03:00", Base.UtcTicks + 1_234_560 },
        { "2026-10-01T11:03:10.000001-03:00", Base.UtcTicks + 10 },
        { "2026-10-01T11:03:10.000000-03:00", Base.UtcTicks },
        { "2026-10-01T14:03:10.999999Z", Base.UtcTicks + 9_999_990 },
        { "2026-10-01T14:03:10.1+00:00", Base.UtcTicks + 1_000_000 },
        { "2026-09-30T23:30:00-03:00", new DateTimeOffset(2026, 10, 1, 2, 30, 0, TimeSpan.Zero).UtcTicks },
        { "2026-10-01T00:30:00-03:00", new DateTimeOffset(2026, 10, 1, 3, 30, 0, TimeSpan.Zero).UtcTicks },
        { "2018-11-04T00:30:00-02:00", new DateTimeOffset(2018, 11, 4, 2, 30, 0, TimeSpan.Zero).UtcTicks },
        { "2026-03-01T00:00:00-03:00", new DateTimeOffset(2026, 3, 1, 3, 0, 0, TimeSpan.Zero).UtcTicks },
        { "2026-10-01T14:03:10.0000000Z", Base.UtcTicks },
        { "2026-10-01T11:03:10.0000000-03:00", Base.UtcTicks },
        { "2026-10-01T11:03:10.1234560-03:00", Base.UtcTicks + 1_234_560 },
        { "2026-10-01T14:03:10.123456000Z", Base.UtcTicks + 1_234_560 },
        { "2026-10-01T14:03:10.500000000+00:00", Base.UtcTicks + 5_000_000 },
        { "2026-10-01T14:03:10.5000000Z", Base.UtcTicks + 5_000_000 },
        { "2026-10-01T14:03:10.100000000Z", Base.UtcTicks + 1_000_000 },
        { "0001-01-01T00:00:00Z", DateTimeOffset.MinValue.UtcTicks },
        { "0001-01-01T00:00:00.0000000+00:00", DateTimeOffset.MinValue.UtcTicks },
        { "0001-01-01T00:00:00-01:00", new DateTimeOffset(1, 1, 1, 1, 0, 0, TimeSpan.Zero).UtcTicks }
    };

    public static TheoryData<string> WithoutTimeZone =>
    [
        "2026-10-01T14:03:10",
        "2026-10-01T14:03:10.5",
        "2026-10-01T14:03:10.123456",
        "2026-10-01T14:03:10.000000",
        "2026-10-01T14:03:10.0000000",
        "2026-10-01T14:03:10.123456000",
        "2026-02-28T23:59:59",
        "0001-01-01T00:00:00"
    ];

    public static TheoryData<string?> Malformed =>
    [
        "2026-10-01",
        "2026-10-01T14:03Z",
        "2026-10-01T14:03",
        "2026-10-01T14:03:10.1234567",
        "2026-10-01T14:03:10.1234567Z",
        "2026-10-01T14:03:10.0000001Z",
        "2026-10-01T14:03:10.1234561-03:00",
        "2026-10-01T14:03:10.123456001Z",
        "2026-10-01T14:03:10.1234560000Z",
        "2026-10-01T14:03:10.0000000000Z",
        "2026-10-01T14:03:10.1234561",
        "2026-10-01T14:03:10.0000000000",
        "2026-10-01T14:03:10.Z",
        "2026-10-01T14:03:10+0000",
        "2026-10-01T14:03:10-0300",
        "2026-10-01T14:03:10+03",
        "2026-10-01T14:03:10-00:00",
        "2026-10-01T11:03:10.5-00:00",
        "2026-10-01T14:03:10+14:01",
        "2026-10-01T14:03:10-14:01",
        "2026-10-01T14:03:10+15:00",
        "2026-10-01T14:03:10-15:00",
        "2026-10-01T14:03:10+25:00",
        "2026-10-01T14:03:10-25:00",
        "2026-10-01T14:03:10+00:60",
        "2026-10-01T14:03:10+99:99",
        "2026-10-01T14:03:10+00:00Z",
        "2026-10-01T14:03:10Z+00:00",
        "2026-10-01T14:03:10 -03:00",
        "2026-10-01T14:03:10 00:00",
        "2026-10-01T24:00:00Z",
        "2026-10-01T23:60:00Z",
        "2026-10-01T23:59:60Z",
        "2026-10-01T24:00:00",
        "2026-02-30T00:00:00Z",
        "2026-02-30T00:00:00",
        "2026-13-01T00:00:00Z",
        "2026-13-01T00:00:00",
        "0000-01-01T00:00:00Z",
        "0001-01-01T00:00:00+01:00",
        "9999-12-31T23:59:59-03:00",
        "2026-10-01t14:03:10Z",
        "2026-10-01T14:03:10z",
        "2026-10-01 14:03:10Z",
        "2026-10-01T14:03:10,5Z",
        "٢٠٢٦-10-01T14:03:10Z",
        "2026-10-01T14:03:10Z\n",
        "2026-10-01T14:03:10\n",
        " 2026-10-01T14:03:10Z",
        "2026-10-01T14:03:10Z ",
        "",
        "ontem",
        null
    ];

    [Theory]
    [MemberData(nameof(AcceptedInstants))]
    public void Read_WithAnInstantThatCarriesAnOffset_ReturnsTheSameInstantInUtc(string text, long expectedTicks)
    {
        var failure = InstantParameterReader.Read(text, out var instant);

        failure.ShouldBe(InstantFailure.None);
        instant.UtcTicks.ShouldBe(expectedTicks);
        instant.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [MemberData(nameof(AcceptedInstants))]
    public void TryRead_WithAnInstantThatCarriesAnOffset_AcceptsIt(string text, long expectedTicks)
    {
        InstantParameterReader.TryRead(text, out var instant).ShouldBeTrue();

        instant.UtcTicks.ShouldBe(expectedTicks);
    }

    [Theory]
    [MemberData(nameof(WithoutTimeZone))]
    public void Read_WithAValidInstantWithoutTimeZone_ReportsTheMissingTimeZone(string text)
    {
        var failure = InstantParameterReader.Read(text, out var instant);

        failure.ShouldBe(InstantFailure.MissingTimeZone);
        instant.ShouldBe(default);
        InstantParameterReader.TryRead(text, out _).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Read_WithAnythingElse_ReportsAMalformedInstant(string? text)
    {
        var failure = InstantParameterReader.Read(text, out var instant);

        failure.ShouldBe(InstantFailure.Malformed);
        instant.ShouldBe(default);
        InstantParameterReader.TryRead(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void Read_WithTheSameInstantWrittenInDifferentOffsets_ReturnsEqualInstants()
    {
        string[] spellings =
        [
            "2026-10-01T14:03:10Z",
            "2026-10-01T14:03:10+00:00",
            "2026-10-01T14:03:10.000000Z",
            "2026-10-01T11:03:10-03:00",
            "2026-10-01T11:03:10.000-03:00",
            "2026-10-01T17:03:10+03:00",
            "2026-10-01T19:33:10+05:30",
            "2026-10-01T00:03:10-14:00"
        ];

        var instants = spellings.Select(spelling =>
        {
            InstantParameterReader.Read(spelling, out var instant).ShouldBe(InstantFailure.None);

            return instant;
        }).ToList();

        instants.ShouldAllBe(instant => instant == Base);
        instants.ShouldAllBe(instant => instant.Offset == TimeSpan.Zero);
    }

    [Fact]
    public void Read_WithTheBrasiliaOffsetAcrossAYearBoundary_MovesTheDateInUtc()
    {
        InstantParameterReader.Read("2026-12-31T22:00:00-03:00", out var instant).ShouldBe(InstantFailure.None);

        instant.ShouldBe(new DateTimeOffset(2027, 1, 1, 1, 0, 0, TimeSpan.Zero));
        instant.Year.ShouldBe(2027);
    }

    [Theory]
    [InlineData("2026-10-01T14:03:10Z")]
    [InlineData("2026-10-01T14:03:10.5Z")]
    [InlineData("2026-10-01T11:03:10.123456-03:00")]
    [InlineData("2026-10-01T11:03:10.1234560-03:00")]
    [InlineData("2026-10-01T14:03:10.123456000+00:00")]
    [InlineData("2026-10-01T14:03:10.5000000Z")]
    [InlineData("2026-10-01T14:03:10.1234561Z")]
    [InlineData("2026-10-01T14:03:10.123456001Z")]
    [InlineData("2026-10-01T14:03:10.1234560000Z")]
    [InlineData("2026-10-01T14:03:10.1234567-03:00")]
    [InlineData("2026-10-01T14:03:10")]
    [InlineData("2026-10-01T14:03:10.5")]
    public void ContractPattern_AgreesWithTheReaderOnTheFractionAndOnTheTimeZone(string text)
    {
        ContractInstant().IsMatch(text).ShouldBe(InstantParameterReader.TryRead(text, out _), text);
    }

    [Fact]
    public void Read_WithTheRoundTripFormatOfDotNet_AcceptsTheSevenDigitsWhenTheSeventhIsZero()
    {
        var written = new DateTimeOffset(2026, 10, 5, 15, 3, 47, TimeSpan.FromHours(-3)).AddTicks(5_654_540);

        var text = written.ToString("o", CultureInfo.InvariantCulture);

        text.ShouldBe("2026-10-05T15:03:47.5654540-03:00");
        InstantParameterReader.Read(text, out var instant).ShouldBe(InstantFailure.None);
        instant.ShouldBe(written);
        instant.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Read_WithTheRoundTripFormatOfDotNet_RefusesTheSeventhDigitThatWouldBeLost()
    {
        var written = new DateTimeOffset(2026, 10, 5, 15, 3, 47, TimeSpan.FromHours(-3)).AddTicks(5_654_541);

        var text = written.ToString("o", CultureInfo.InvariantCulture);

        text.ShouldBe("2026-10-05T15:03:47.5654541-03:00");
        InstantParameterReader.Read(text, out _).ShouldBe(InstantFailure.Malformed);
    }

    [Fact]
    public void Read_WithTheRoundTripFormatOfTheDefaultDateTimeOffset_ReadsTheFirstInstantOfTheCalendar()
    {
        var text = default(DateTimeOffset).ToString("o", CultureInfo.InvariantCulture);

        text.ShouldBe("0001-01-01T00:00:00.0000000+00:00");
        InstantParameterReader.Read(text, out var instant).ShouldBe(InstantFailure.None);
        instant.ShouldBe(default);
    }

    [Fact]
    public void Read_WithSevenToNineDigitsOfZeroPadding_ReturnsTheSameInstantAsTheSixDigitSpelling()
    {
        string[] spellings =
        [
            "2026-10-01T11:03:10.123456-03:00",
            "2026-10-01T11:03:10.1234560-03:00",
            "2026-10-01T11:03:10.12345600-03:00",
            "2026-10-01T11:03:10.123456000-03:00",
            "2026-10-01T14:03:10.123456Z",
            "2026-10-01T14:03:10.123456000+00:00"
        ];

        var instants = spellings.Select(spelling =>
        {
            InstantParameterReader.Read(spelling, out var instant).ShouldBe(InstantFailure.None);

            return instant;
        }).ToList();

        instants.ShouldAllBe(instant => instant == instants[0]);
        instants[0].UtcTicks.ShouldBe(Base.UtcTicks + 1_234_560);
    }

    [Fact]
    public void Read_NeverLosesTheMicrosecondOfTheFraction()
    {
        InstantParameterReader.Read("2026-10-01T11:03:10.000001-03:00", out var first).ShouldBe(InstantFailure.None);
        InstantParameterReader.Read("2026-10-01T11:03:10.000002-03:00", out var second).ShouldBe(InstantFailure.None);

        (second - first).ShouldBe(TimeSpan.FromTicks(10));
    }

    [GeneratedRegex(ContractPatterns.Instant, RegexOptions.CultureInvariant)]
    private static partial Regex ContractInstant();
}
