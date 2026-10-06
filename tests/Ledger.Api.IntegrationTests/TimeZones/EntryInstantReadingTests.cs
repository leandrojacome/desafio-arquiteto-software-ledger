using System.Text;
using Ledger.Api.Validation;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.TimeZones;

[Trait("Category", "Unit")]
public sealed class EntryInstantReadingTests
{
    private const string MissingTimeZoneMessage =
        "Informe o fuso horário no campo 'occurredAt', por exemplo 'Z' ou '-03:00'.";

    private const string InvalidInstantMessage =
        "O campo 'occurredAt' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo.";

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 14, 3, 11, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);

    public static TheoryData<string, long> SameInstantInDifferentSpellings => new()
    {
        { "2026-10-01T14:03:10Z", new DateTimeOffset(2026, 10, 1, 14, 3, 10, TimeSpan.Zero).UtcTicks },
        { "2026-10-01T14:03:10+00:00", new DateTimeOffset(2026, 10, 1, 14, 3, 10, TimeSpan.Zero).UtcTicks },
        { "2026-10-01T11:03:10-03:00", new DateTimeOffset(2026, 10, 1, 14, 3, 10, TimeSpan.Zero).UtcTicks },
        { "2026-10-01T19:33:10+05:30", new DateTimeOffset(2026, 10, 1, 14, 3, 10, TimeSpan.Zero).UtcTicks },
        { "2026-10-01T00:03:10-14:00", new DateTimeOffset(2026, 10, 1, 14, 3, 10, TimeSpan.Zero).UtcTicks },
        { "2026-10-01T11:03:10.250000-03:00", new DateTimeOffset(2026, 10, 1, 14, 3, 10, TimeSpan.Zero).UtcTicks + 2_500_000 }
    };

    private RegisterEntryRequestReader Reader(int toleranceMinutes = 5) =>
        new(_time, Options.Create(new LedgerOptions { OccurredAtFutureToleranceMinutes = toleranceMinutes }));

    private ReadResult<RegisterEntryInput> Read(string occurredAt) =>
        Reader().Read(Encoding.UTF8.GetBytes(
            $"{{\"type\":\"CREDIT\",\"amount\":\"10.00\",\"currency\":\"BRL\",\"occurredAt\":\"{occurredAt}\"}}"));

    [Theory]
    [MemberData(nameof(SameInstantInDifferentSpellings))]
    public void OccurredAt_WithAnyOffset_IsReadAsTheSameInstantInUtc(string text, long expectedTicks)
    {
        var result = Read(text);

        result.IsValid.ShouldBeTrue();

        var occurredAt = result.Value.OccurredAt.ShouldNotBeNull();

        occurredAt.UtcTicks.ShouldBe(expectedTicks);
        occurredAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("2026-10-01T14:03:10")]
    [InlineData("2026-10-01T14:03:10.5")]
    [InlineData("2026-10-01T14:03:10.123456")]
    public void OccurredAt_WithoutTimeZone_IsRefusedWithTheGuidanceToInformTheTimeZone(string text)
    {
        var issue = Read(text).Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("occurredAt");
        issue.Reason.ShouldBe("MISSING_TIME_ZONE");
        issue.Message.ShouldBe(MissingTimeZoneMessage);
    }

    [Theory]
    [InlineData("2026-10-01T14:03:10+25:00")]
    [InlineData("2026-10-01T14:03:10-15:00")]
    [InlineData("2026-10-01T14:03:10+14:01")]
    [InlineData("2026-10-01T14:03:10+00:60")]
    [InlineData("2026-10-01T14:03:10-00:00")]
    [InlineData("2026-10-01T14:03:10+0300")]
    [InlineData("2026-10-01T14:03:10.1234567Z")]
    [InlineData("2026-10-01T14:03Z")]
    [InlineData("2026-13-01T14:03:10Z")]
    [InlineData("2026-13-01T14:03:10")]
    public void OccurredAt_WithAnInvalidOffsetOrShape_IsRefusedAsAnInvalidFormat(string text)
    {
        var issue = Read(text).Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("occurredAt");
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe(InvalidInstantMessage);
    }

    [Theory]
    [InlineData("2026-10-01T11:08:11-03:00")]
    [InlineData("2026-10-01T14:08:11Z")]
    [InlineData("2026-10-01T14:08:11+00:00")]
    [InlineData("2026-10-01T19:38:11+05:30")]
    [InlineData("2026-10-01T23:03:11+14:00")]
    [InlineData("2026-10-02T04:03:11+14:00")]
    [InlineData("2026-10-01T00:00:00-14:00")]
    public void OccurredAt_UpToTheToleranceAheadOfTheClockInUtc_IsAccepted(string text)
    {
        Read(text).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("2026-10-01T11:08:12-03:00")]
    [InlineData("2026-10-01T14:08:12Z")]
    [InlineData("2026-10-01T19:38:12+05:30")]
    [InlineData("2026-10-01T05:08:12-14:00")]
    [InlineData("2026-10-01T19:08:11-14:00")]
    [InlineData("2026-10-01T11:08:11.000001-03:00")]
    public void OccurredAt_BeyondTheToleranceAheadOfTheClockInUtc_IsInTheFuture(string text)
    {
        var issue = Read(text).Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("occurredAt");
        issue.Reason.ShouldBe("IN_THE_FUTURE");
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("0001-01-01T00:00:00+00:00")]
    [InlineData("0001-01-01T00:00:00.0000000+00:00")]
    [InlineData("0001-01-01T00:00:00-03:00")]
    [InlineData("1900-01-01T00:00:00Z")]
    [InlineData("1969-12-31T23:59:59.999999Z")]
    [InlineData("1969-12-31T20:59:59.999999-03:00")]
    public void OccurredAt_BeforeTheFirstInstantOf1970_IsOutOfRange(string text)
    {
        var issue = Read(text).Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("occurredAt");
        issue.Reason.ShouldBe("OUT_OF_RANGE");
        issue.Message.ShouldBe("O campo 'occurredAt' está fora da faixa permitida.");
    }

    [Fact]
    public void OccurredAt_OfTheDefaultDateTimeOffsetOfDotNet_IsOutOfRange()
    {
        var text = default(DateTimeOffset).ToString("o", System.Globalization.CultureInfo.InvariantCulture);

        var issue = Read(text).Issues.ShouldHaveSingleItem();

        issue.Reason.ShouldBe("OUT_OF_RANGE");
    }

    [Theory]
    [InlineData("1970-01-01T00:00:00Z", 0)]
    [InlineData("1970-01-01T00:00:00+00:00", 0)]
    [InlineData("1969-12-31T21:00:00-03:00", 0)]
    [InlineData("1970-01-01T00:00:00.000001Z", 10)]
    public void OccurredAt_AtOrAfterTheFirstInstantOf1970_IsAccepted(string text, long ticksAfterTheEpoch)
    {
        var result = Read(text);

        result.IsValid.ShouldBeTrue();
        result.Value.OccurredAt.ShouldNotBeNull().UtcTicks.ShouldBe(DateTimeOffset.UnixEpoch.UtcTicks + ticksAfterTheEpoch);
    }

    [Theory]
    [InlineData("2026-10-01T11:03:10.0000000-03:00")]
    [InlineData("2026-10-01T11:03:10.1234560-03:00")]
    [InlineData("2026-10-01T14:03:10.123456000Z")]
    public void OccurredAt_WithZeroPaddingBeyondTheSixthDecimal_IsAcceptedWithoutLosingPrecision(string text)
    {
        var result = Read(text);

        result.IsValid.ShouldBeTrue();
        (result.Value.OccurredAt.ShouldNotBeNull().UtcTicks % 10).ShouldBe(0);
    }

    [Theory]
    [InlineData("2026-10-01T11:03:10.5654541-03:00")]
    [InlineData("2026-10-01T14:03:10.123456789Z")]
    [InlineData("2026-10-01T14:03:10.1234560000Z")]
    public void OccurredAt_WithADigitBeyondTheSixthDecimalThatIsNotZero_IsRefusedWithTheGuidanceAboutTheDecimals(string text)
    {
        var issue = Read(text).Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("occurredAt");
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe(InvalidInstantMessage);
        issue.Message.ShouldContain("6 casas decimais de segundo");
    }

    [Fact]
    public void OccurredAt_AWallClockAheadOfUtcThatIsStillInThePast_IsNotInTheFuture()
    {
        Read("2026-10-02T03:00:00+14:00").IsValid.ShouldBeTrue();
        Read("2026-10-01T13:30:00+00:00").IsValid.ShouldBeTrue();
    }

    [Fact]
    public void OccurredAt_AWallClockBehindUtcThatIsAlreadyInTheFuture_IsInTheFuture()
    {
        var issue = Read("2026-10-01T06:00:00-14:00").Issues.ShouldHaveSingleItem();

        issue.Reason.ShouldBe("IN_THE_FUTURE");
    }

    [Fact]
    public void OccurredAt_FollowsTheConfiguredToleranceInUtcWhateverTheOffset()
    {
        var body = Encoding.UTF8.GetBytes(
            "{\"type\":\"CREDIT\",\"amount\":\"10.00\",\"currency\":\"BRL\",\"occurredAt\":\"2026-10-01T11:08:11-03:00\"}");

        Reader(5).Read(body).IsValid.ShouldBeTrue();
        Reader(4).Read(body).IsValid.ShouldBeFalse();
        Reader(0).Read(body).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("2026-10-01T14:03:10")]
    [InlineData("2026-10-01T14:03:10+25:00")]
    [InlineData("ontem-ao-meio-dia-7f3a")]
    public void OccurredAt_WhenRefused_NeverRepeatsTheReceivedValue(string text)
    {
        var issue = Read(text).Issues.ShouldHaveSingleItem();

        issue.Message.ShouldNotContain("14:03", Case.Sensitive);
        issue.Message.ShouldNotContain("25:00", Case.Sensitive);
        issue.Message.ShouldNotContain("7f3a", Case.Sensitive);
    }

    [Fact]
    public void OccurredAt_WhenAbsentOrNull_IsReadAsAbsent()
    {
        var absent = Reader().Read(Encoding.UTF8.GetBytes("{\"type\":\"CREDIT\",\"amount\":\"10.00\",\"currency\":\"BRL\"}"));
        var explicitNull = Reader().Read(Encoding.UTF8.GetBytes(
            "{\"type\":\"CREDIT\",\"amount\":\"10.00\",\"currency\":\"BRL\",\"occurredAt\":null}"));

        absent.Value.OccurredAt.ShouldBeNull();
        explicitNull.Value.OccurredAt.ShouldBeNull();
    }

    [Fact]
    public void OccurredAt_WithAMissingTimeZoneAndAnotherWrongField_ReportsBothInTheContractOrder()
    {
        var result = Reader().Read(Encoding.UTF8.GetBytes(
            "{\"type\":\"TRANSFER\",\"amount\":\"10.00\",\"currency\":\"BRL\",\"occurredAt\":\"2026-10-01T14:03:10\"}"));

        result.Issues.Select(issue => (issue.Field, issue.Reason)).ShouldBe(
            [("type", "NOT_ALLOWED"), ("occurredAt", "MISSING_TIME_ZONE")]);
    }
}
