using Ledger.Api.Reads;
using Ledger.Api.Validation;
using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Reads;

[Trait("Category", "Unit")]
public sealed class StatementQueryReaderTests
{
    private const string ValidCursor = "VALID-CURSOR-FOR-THE-FAKE-PROTECTOR";

    private static readonly AccountId Account = AccountId.From(Guid.CreateVersion7()).Value;

    private static readonly StatementPosition KnownPosition =
        new(new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero).AddTicks(4_829_130), 1843);

    [Fact]
    public void Read_WithoutParameters_UsesTheDefaultLimitAndNothingElse()
    {
        var read = Reader().Read(Account, null);

        read.IsValid.ShouldBeTrue();
        read.Value.Limit.ShouldBe(50);
        read.Value.From.ShouldBeNull();
        read.Value.To.ShouldBeNull();
        read.Value.Cursor.ShouldBeNull();
    }

    [Fact]
    public void Read_WithConfiguredLimits_UsesThem()
    {
        var reader = Reader(defaultLimit: 20, maxLimit: 30);

        reader.Read(Account, "?limit=30").Value.Limit.ShouldBe(30);
        reader.Read(Account, null).Value.Limit.ShouldBe(20);
        reader.Read(Account, "?limit=31").Issues.ShouldHaveSingleItem().Message.ShouldBe("O campo 'limit' deve estar entre 1 e 30.");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("5.5")]
    [InlineData("1e2")]
    [InlineData("+5")]
    [InlineData("%205")]
    [InlineData("5%20")]
    [InlineData("")]
    [InlineData("--5")]
    [InlineData("0x10")]
    [InlineData("%D9%A5")]
    public void Read_WithALimitThatIsNotAnInteger_IsInvalidFormat(string limit)
    {
        var read = Reader().Read(Account, $"?limit={limit}");

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("limit");
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe("O campo 'limit' deve ser um número inteiro.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-0")]
    [InlineData("201")]
    [InlineData("99999999999")]
    [InlineData("0000000000050")]
    [InlineData("-99999999999999999999999")]
    public void Read_WithAnIntegerLimitOutsideTheRange_IsOutOfRange(string limit)
    {
        var read = Reader().Read(Account, $"?limit={limit}");

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("limit");
        issue.Reason.ShouldBe("OUT_OF_RANGE");
        issue.Message.ShouldBe("O campo 'limit' deve estar entre 1 e 200.");
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("200", 200)]
    [InlineData("050", 50)]
    [InlineData("000000050", 50)]
    [InlineData("7", 7)]
    public void Read_WithALimitInsideTheRange_ReturnsIt(string limit, int expected)
    {
        var read = Reader().Read(Account, $"?limit={limit}");

        read.IsValid.ShouldBeTrue();
        read.Value.Limit.ShouldBe(expected);
    }

    [Fact]
    public void Read_WithFromAndToInTheStrictGrammar_ReturnsBothInstants()
    {
        var read = Reader().Read(Account, "?from=2026-10-01T03:00:00Z&to=2026-10-02T03:00:00.5%2B00:00");

        read.IsValid.ShouldBeTrue();
        read.Value.From.ShouldBe(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero));
        read.Value.To.ShouldBe(new DateTimeOffset(2026, 10, 2, 3, 0, 0, 500, TimeSpan.Zero));
    }

    [Fact]
    public void Read_WithFromAndToInTheBrasiliaOffset_ReturnsBothInstantsInUtc()
    {
        var read = Reader().Read(Account, "?from=2026-10-01T00:00:00-03:00&to=2026-10-02T00:00:00.5-03:00");

        read.IsValid.ShouldBeTrue();
        read.Value.From.ShouldBe(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero));
        read.Value.To.ShouldBe(new DateTimeOffset(2026, 10, 2, 3, 0, 0, 500, TimeSpan.Zero));
        read.Value.From.ShouldNotBeNull().Offset.ShouldBe(TimeSpan.Zero);
        read.Value.To.ShouldNotBeNull().Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("from", "")]
    [InlineData("from", "ontem")]
    [InlineData("from", "2026-10-01T03:00:00.1234567Z")]
    [InlineData("from", "2026-10-01T03:00:00+25:00")]
    [InlineData("to", "")]
    [InlineData("to", "2026-10-01")]
    [InlineData("to", "2026-02-30T00:00:00Z")]
    public void Read_WithAnInstantOutsideTheGrammar_IsInvalidFormatOnThatField(string name, string value)
    {
        var read = Reader().Read(Account, $"?{name}={Uri.EscapeDataString(value)}");

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe(name);
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe(
            $"O campo '{name}' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo.");
    }

    [Theory]
    [InlineData("from", "2026-10-01T03:00:00")]
    [InlineData("to", "2026-10-01T03:00:00.5")]
    public void Read_WithAnInstantWithoutTimeZone_IsMissingTimeZoneOnThatField(string name, string value)
    {
        var read = Reader().Read(Account, $"?{name}={Uri.EscapeDataString(value)}");

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe(name);
        issue.Reason.ShouldBe("MISSING_TIME_ZONE");
        issue.Message.ShouldBe($"Informe o fuso horário no campo '{name}', por exemplo 'Z' ou '-03:00'.");
    }

    [Theory]
    [InlineData("2026-10-01T10:00:00Z", "2026-10-01T10:00:00Z")]
    [InlineData("2026-10-02T10:00:00Z", "2026-10-01T10:00:00Z")]
    [InlineData("2026-10-01T10:00:00.000001Z", "2026-10-01T10:00:00Z")]
    public void Read_WithFromEqualOrAfterTo_IsFromAfterToOnFrom(string from, string to)
    {
        var read = Reader().Read(Account, $"?from={from}&to={to}");

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("from");
        issue.Reason.ShouldBe("FROM_AFTER_TO");
        issue.Message.ShouldBe("O campo 'from' deve ser anterior a 'to'.");
    }

    [Fact]
    public void Read_WithFromOneMicrosecondBeforeTo_IsValid()
    {
        var read = Reader().Read(Account, "?from=2026-10-01T10:00:00Z&to=2026-10-01T10:00:00.000001Z");

        read.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Read_WithAValidCursor_ReturnsThePosition()
    {
        var read = Reader().Read(Account, $"?cursor={ValidCursor}");

        read.IsValid.ShouldBeTrue();
        read.Value.Cursor.ShouldBe(KnownPosition);
    }

    [Theory]
    [InlineData("?cursor=lixo")]
    [InlineData("?cursor=")]
    [InlineData("?cursor")]
    [InlineData("?cursor=VALID-CURSOR-FOR-THE-FAKE-PROTECTOR&cursor=VALID-CURSOR-FOR-THE-FAKE-PROTECTOR")]
    [InlineData("?cursor=0123456789012345678901234567890123456789012345678901234567890123456789")]
    [InlineData("?cursor=AQAGXMfgSmQhAAAAAAAABzPzGOCjl5YS7PXaNwBbM93l=")]
    public void Read_WithACursorThatIsNotAccepted_IsInvalidCursorWithTheSameMessage(string queryString)
    {
        var read = Reader().Read(Account, queryString);

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("cursor");
        issue.Reason.ShouldBe("INVALID_CURSOR");
        issue.Message.ShouldBe("O cursor não é válido para esta conta.");
    }

    [Fact]
    public void Read_WithACursorOfSixtyFourCharacters_StillReachesTheProtector()
    {
        var protector = new FakeCursorProtector();
        var cursor = new string('A', 64);

        var read = Reader(protector: protector).Read(Account, $"?cursor={cursor}");

        read.Issues.ShouldHaveSingleItem().Reason.ShouldBe("INVALID_CURSOR");
        protector.Received.ShouldBe([cursor]);
    }

    [Fact]
    public void Read_WithACursorLongerThanSixtyFourCharactersOrEmpty_NeverReachesTheProtector()
    {
        var protector = new FakeCursorProtector();

        Reader(protector: protector).Read(Account, $"?cursor={new string('A', 65)}");
        Reader(protector: protector).Read(Account, "?cursor=");

        protector.Received.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("?from=2026-10-01T03:00:00Z&from=2026-10-01T04:00:00Z", "from")]
    [InlineData("?to=2026-10-01T03:00:00Z&to=2026-10-01T04:00:00Z", "to")]
    [InlineData("?limit=5&limit=6", "limit")]
    public void Read_WithARepeatedParameter_IsTheFormatErrorOfThatParameter(string queryString, string field)
    {
        var read = Reader().Read(Account, queryString);

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe(field);
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe("O parâmetro foi enviado mais de uma vez.");
    }

    [Theory]
    [InlineData("?asOf=2026-10-01T03:00:00Z", "asOf")]
    [InlineData("?FROM=2026-10-01T03:00:00Z", "FROM")]
    [InlineData("?page=2", "page")]
    public void Read_WithAnUnknownName_IsUnknownField(string queryString, string field)
    {
        var read = Reader().Read(Account, queryString);

        var issue = read.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe(field);
        issue.Reason.ShouldBe("UNKNOWN_FIELD");
        issue.Message.ShouldBe("O parâmetro não é suportado.");
    }

    [Fact]
    public void Read_WithSeveralProblems_ReportsAllOfThemInTheContractOrder()
    {
        var read = Reader().Read(Account, "?cursor=lixo&limit=0&to=nope&from=ontem&foo=1&a%0Ab=2");

        read.Issues.Select(issue => (issue.Field, issue.Reason)).ShouldBe(
        [
            ("foo", "UNKNOWN_FIELD"),
            ("(unknown)", "UNKNOWN_FIELD"),
            ("from", "INVALID_FORMAT"),
            ("to", "INVALID_FORMAT"),
            ("limit", "OUT_OF_RANGE"),
            ("cursor", "INVALID_CURSOR")
        ]);
    }

    [Fact]
    public void Read_NeverEchoesTheValueReceived()
    {
        var read = Reader().Read(
            Account,
            "?from=SENTINEL-FROM&to=SENTINEL-TO&limit=SENTINEL-LIMIT&cursor=SENTINEL-CURSOR&sentinelname=SENTINEL-OTHER");

        var text = string.Join(' ', read.Issues.Select(issue => $"{issue.Field} {issue.Reason} {issue.Message}"));

        read.Issues.Count.ShouldBe(5);
        text.ShouldNotContain("SENTINEL", Case.Sensitive);
    }

    [Fact]
    public void Read_HandsTheAccountOfTheRouteToTheProtector()
    {
        var protector = new FakeCursorProtector();

        Reader(protector: protector).Read(Account, $"?cursor={ValidCursor}");

        protector.Accounts.ShouldBe([Account]);
    }

    private static StatementQueryReader Reader(
        int defaultLimit = 50,
        int maxLimit = 200,
        FakeCursorProtector? protector = null) =>
        new(
            protector ?? new FakeCursorProtector(),
            Options.Create(new StatementOptions { DefaultLimit = defaultLimit, MaxLimit = maxLimit }));

    private sealed class FakeCursorProtector : IStatementCursorProtector
    {
        private readonly List<string> _received = [];
        private readonly List<AccountId> _accounts = [];

        public IReadOnlyList<string> Received => _received;

        public IReadOnlyList<AccountId> Accounts => _accounts;

        public string Protect(AccountId accountId, StatementPosition position) => ValidCursor;

        public Result<StatementPosition> Unprotect(AccountId accountId, string cursor)
        {
            _received.Add(cursor);
            _accounts.Add(accountId);

            return cursor == ValidCursor ? KnownPosition : StatementErrors.InvalidCursor;
        }
    }
}
