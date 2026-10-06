using Ledger.Api.Reads;
using Ledger.Api.Validation;
using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.TimeZones;

[Trait("Category", "Unit")]
public sealed class QueryInstantReadingTests
{
    private const string MissingFromMessage =
        "Informe o fuso horário no campo 'from', por exemplo 'Z' ou '-03:00'.";

    private const string MissingToMessage =
        "Informe o fuso horário no campo 'to', por exemplo 'Z' ou '-03:00'.";

    private const string InvalidFromMessage =
        "O campo 'from' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo.";

    private static readonly AccountId Account = AccountId.From(Guid.CreateVersion7()).Value;

    private static readonly DateTimeOffset Instant = new(2026, 10, 1, 14, 3, 11, TimeSpan.Zero);

    [Theory]
    [InlineData("?asOf=2026-10-01T11:03:11-03:00", 0)]
    [InlineData("?asOf=2026-10-01T11:03:11.482913-03:00", 4_829_130)]
    [InlineData("?asOf=2026-10-01T11%3A03%3A11-03%3A00", 0)]
    [InlineData("?asOf=2026-10-01T17:03:11%2B03:00", 0)]
    [InlineData("?asOf=2026-10-01T19:33:11%2B05:30", 0)]
    [InlineData("?asOf=2026-10-01T14:03:11%2B00:00", 0)]
    [InlineData("?asOf=2026-10-01T14:03:11Z", 0)]
    [InlineData("?asOf=2026-10-01T00:03:11-14:00", 0)]
    public void Balance_WithAnOffsetInTheAsOf_ReadsTheInstantInUtc(string queryString, long extraTicks)
    {
        var read = BalanceQueryReader.Read(queryString);

        read.IsValid.ShouldBeTrue();

        var asOf = read.AsOf.ShouldNotBeNull();

        asOf.ShouldBe(Instant.AddTicks(extraTicks));
        asOf.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("?asOf=2026-10-01T14:03:11")]
    [InlineData("?asOf=2026-10-01T14:03:11.5")]
    [InlineData("?asOf=2026-10-01T14:03:11%2B25:00")]
    [InlineData("?asOf=2026-10-01T14:03:11-00:00")]
    [InlineData("?asOf=2026-10-01T14:03:11-15:00")]
    [InlineData("?asOf=2026-10-01T17:03:11+03:00")]
    [InlineData("?asOf=2026-10-01T14:03:11+00:00")]
    [InlineData("?asOf=2026-10-01T14:03:11%2B0300")]
    [InlineData("?asOf=2026-10-01T14:03:11.1234567-03:00")]
    public void Balance_WithAnAsOfThatIsNotAnInstantWithAValidOffset_IsRejectedAsInvalidAsOf(string queryString)
    {
        var read = BalanceQueryReader.Read(queryString);

        read.IsValid.ShouldBeFalse();
        read.Error.ShouldBe(BalanceErrors.InvalidAsOf);
        read.Issues.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("?asOf=2026-10-01T11:03:11.4829130-03:00", 4_829_130)]
    [InlineData("?asOf=2026-10-01T14:03:11.482913000Z", 4_829_130)]
    [InlineData("?asOf=2026-10-01T14:03:11.0000000Z", 0)]
    public void Balance_WithZeroPaddingBeyondTheSixthDecimal_ReadsTheSameInstant(string queryString, long extraTicks)
    {
        var read = BalanceQueryReader.Read(queryString);

        read.IsValid.ShouldBeTrue();
        read.AsOf.ShouldBe(Instant.AddTicks(extraTicks));
    }

    [Theory]
    [InlineData("?asOf=2026-10-01T11:03:11.4829131-03:00")]
    [InlineData("?asOf=2026-10-01T14:03:11.482913001Z")]
    [InlineData("?asOf=2026-10-01T14:03:11.4829130000Z")]
    public void Balance_WithADigitBeyondTheSixthDecimalThatIsNotZero_IsRejectedAsInvalidAsOf(string queryString)
    {
        var read = BalanceQueryReader.Read(queryString);

        read.IsValid.ShouldBeFalse();
        read.Error.ShouldBe(BalanceErrors.InvalidAsOf);
    }

    [Fact]
    public void InvalidAsOfMessages_SayTheNumberOfDecimalsThatTheReaderAccepts()
    {
        var decimals = $"{EntryRequestLimits.MaxFractionDigitsOfInstant} casas decimais de segundo";

        BalanceErrors.InvalidAsOf.Message.ShouldContain(decimals);
        FieldIssues.InvalidInstant("from").Message.ShouldContain(decimals);
    }

    [Fact]
    public void Statement_WithBoundsThatCarryZeroPaddingBeyondTheSixthDecimal_ReadsThemInUtc()
    {
        var read = Reader().Read(
            Account,
            "?from=2026-10-01T00:00:00.0000000-03:00&to=2026-10-02T03:00:00.000000000Z");

        read.IsValid.ShouldBeTrue();
        read.Value.From.ShouldBe(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero));
        read.Value.To.ShouldBe(new DateTimeOffset(2026, 10, 2, 3, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Balance_WithTheSameInstantInTwoOffsets_ReadsEqualInstants()
    {
        var zulu = BalanceQueryReader.Read("?asOf=2026-10-01T14:03:11Z");
        var brasilia = BalanceQueryReader.Read("?asOf=2026-10-01T11:03:11-03:00");

        brasilia.AsOf.ShouldBe(zulu.AsOf);
        brasilia.AsOf.ShouldNotBeNull().UtcTicks.ShouldBe(zulu.AsOf.ShouldNotBeNull().UtcTicks);
    }

    [Fact]
    public void Statement_WithBoundsInTheBrasiliaOffset_ReadsBothInUtc()
    {
        var read = Reader().Read(Account, "?from=2026-10-01T00:00:00-03:00&to=2026-10-02T00:00:00-03:00");

        read.IsValid.ShouldBeTrue();
        read.Value.From.ShouldBe(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero));
        read.Value.To.ShouldBe(new DateTimeOffset(2026, 10, 2, 3, 0, 0, TimeSpan.Zero));
        read.Value.From.ShouldNotBeNull().Offset.ShouldBe(TimeSpan.Zero);
        read.Value.To.ShouldNotBeNull().Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Statement_WithBoundsInDifferentOffsets_ReadsThemInTheSameLine()
    {
        var read = Reader().Read(
            Account,
            "?from=2026-10-01T03:00:00Z&to=2026-10-02T08:30:00.25%2B05:30");

        read.IsValid.ShouldBeTrue();
        read.Value.From.ShouldBe(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero));
        read.Value.To.ShouldBe(new DateTimeOffset(2026, 10, 2, 3, 0, 0, 250, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("from", "2026-10-01T00:00:00")]
    [InlineData("from", "2026-10-01T00:00:00.5")]
    [InlineData("to", "2026-10-01T00:00:00")]
    public void Statement_WithABoundWithoutTimeZone_ReportsMissingTimeZoneOnThatField(string name, string value)
    {
        var read = Reader().Read(Account, $"?{name}={Uri.EscapeDataString(value)}");

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe(name);
        issue.Reason.ShouldBe("MISSING_TIME_ZONE");
        issue.Message.ShouldBe(name == "from" ? MissingFromMessage : MissingToMessage);
    }

    [Theory]
    [InlineData("2026-10-01T00:00:00+25:00")]
    [InlineData("2026-10-01T00:00:00-00:00")]
    [InlineData("2026-10-01T00:00:00+14:01")]
    [InlineData("2026-10-01")]
    [InlineData("ontem")]
    [InlineData("")]
    public void Statement_WithABoundThatIsNotAnInstantWithAValidOffset_ReportsAnInvalidFormat(string value)
    {
        var read = Reader().Read(Account, $"?from={Uri.EscapeDataString(value)}");

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("from");
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe(InvalidFromMessage);
    }

    [Theory]
    [InlineData("2026-10-01T03:00:00Z", "2026-10-01T00:00:00-03:00")]
    [InlineData("2026-10-01T00:00:00-03:00", "2026-10-01T03:00:00Z")]
    [InlineData("2026-10-01T08:30:00+05:30", "2026-10-01T00:00:00-03:00")]
    [InlineData("2026-10-01T00:00:00-03:00", "2026-09-30T23:59:59-03:00")]
    public void Statement_WithFromEqualOrAfterToInUtc_IsFromAfterToWhateverTheOffsets(string from, string to)
    {
        var read = Reader().Read(Account, $"?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("from");
        issue.Reason.ShouldBe("FROM_AFTER_TO");
    }

    [Fact]
    public void Statement_WithFromOneMicrosecondBeforeToAcrossOffsets_IsValid()
    {
        var read = Reader().Read(
            Account,
            "?from=2026-10-01T00:00:00-03:00&to=2026-10-01T03:00:00.000001Z");

        read.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Statement_WithBoundsWrittenInTwoWays_ReadsTheSameInput()
    {
        var zulu = Reader().Read(Account, "?from=2026-10-01T03:00:00Z&to=2026-10-02T03:00:00Z&limit=10");
        var brasilia = Reader().Read(Account, "?from=2026-10-01T00:00:00-03:00&to=2026-10-02T00:00:00-03:00&limit=10");

        brasilia.Value.ShouldBe(zulu.Value);
    }

    [Fact]
    public void Statement_WithTheRepeatedBoundInAnotherOffset_IsStillRepeated()
    {
        var read = Reader().Read(Account, "?from=2026-10-01T03:00:00Z&from=2026-10-01T00:00:00-03:00");

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("from");
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe("O parâmetro foi enviado mais de uma vez.");
    }

    [Fact]
    public void Statement_WithSeveralBoundProblems_ReportsTheMissingTimeZoneAndTheInvalidOffsetInOrder()
    {
        var read = Reader().Read(Account, "?to=2026-10-01T00:00:00&from=2026-10-01T00:00:00%2B25:00");

        read.Issues.Select(issue => (issue.Field, issue.Reason)).ShouldBe(
            [("from", "INVALID_FORMAT"), ("to", "MISSING_TIME_ZONE")]);
    }

    private static StatementQueryReader Reader() =>
        new(new RejectingCursorProtector(), Options.Create(new StatementOptions { DefaultLimit = 50, MaxLimit = 200 }));

    private sealed class RejectingCursorProtector : IStatementCursorProtector
    {
        public string Protect(AccountId accountId, StatementPosition position) => string.Empty;

        public Result<StatementPosition> Unprotect(AccountId accountId, string cursor) => StatementErrors.InvalidCursor;
    }
}
