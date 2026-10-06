using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Infrastructure.Security;

namespace Ledger.Api.IntegrationTests.Reads;

[Trait("Category", "Integration")]
public sealed class ReadValidationTests(LedgerApiFactory factory) : IClassFixture<LedgerApiFactory>
{
    private const string OtherKey = "AgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhscHR4fICE=";
    private const string CursorMessage = "O cursor não é válido para esta conta.";

    private static readonly AccountId Account = AccountId.From(Guid.CreateVersion7()).Value;

    private static readonly StatementPosition Position =
        new(new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero).AddTicks(4_829_130), 1843);

    [Fact]
    public async Task Statement_WithAllTheParametersInvalid_ReturnsOne400WithTheFourProblemsInTheContractOrder()
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.StatementAsync(
            Account,
            "cursor=lixo&limit=abc&to=2026-10-01T10:00:00&from=ontem");

        response.ShouldBeProblem(
            HttpStatusCode.BadRequest,
            "VALIDATION_FAILED",
            "Falha na validação da requisição",
            ReadApiClient.StatementPath(Account));
        response.Text("detail").ShouldBe("Um ou mais campos são inválidos.");
        response.Errors().Select(error => (error.Field, error.Reason)).ShouldBe(
        [
            ("from", "INVALID_FORMAT"),
            ("to", "MISSING_TIME_ZONE"),
            ("limit", "INVALID_FORMAT"),
            ("cursor", "INVALID_CURSOR")
        ]);
        response.Header("Cache-Control").ShouldBe("no-store");
        response.ShouldCarryNoInternals();
    }

    [Theory]
    [InlineData("0", "OUT_OF_RANGE")]
    [InlineData("201", "OUT_OF_RANGE")]
    [InlineData("-1", "OUT_OF_RANGE")]
    [InlineData("9999999999", "OUT_OF_RANGE")]
    [InlineData("5.5", "INVALID_FORMAT")]
    [InlineData("1e2", "INVALID_FORMAT")]
    [InlineData("", "INVALID_FORMAT")]
    [InlineData("abc", "INVALID_FORMAT")]
    [InlineData("+5", "INVALID_FORMAT")]
    public async Task Statement_WithAnInvalidLimit_ReportsTheReasonOfTheRule(string limit, string reason)
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.StatementAsync(Account, $"limit={Uri.EscapeDataString(limit)}");

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);

        var error = response.Errors().ShouldHaveSingleItem();

        error.Field.ShouldBe("limit");
        error.Reason.ShouldBe(reason);
    }

    [Theory]
    [InlineData("from=2026-10-01T10:00:00Z&to=2026-10-01T10:00:00Z")]
    [InlineData("from=2026-10-02T10:00:00Z&to=2026-10-01T10:00:00Z")]
    public async Task Statement_WithFromEqualOrAfterTo_ReportsFromAfterToOnFrom(string query)
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.StatementAsync(Account, query);

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        response.Errors().ShouldHaveSingleItem().ShouldBe(("from", "FROM_AFTER_TO", "O campo 'from' deve ser anterior a 'to'."));
    }

    [Fact]
    public async Task Statement_WithAnAdulteratedCursorOrOneOfAnotherAccountOrKey_ReportsTheSameProblem()
    {
        using var client = ReadApiClient.For(factory);
        var valid = Protector(TestConfiguration.CursorSigningKey).Protect(Account, Position);
        var otherAccount = Protector(TestConfiguration.CursorSigningKey)
            .Protect(AccountId.From(Guid.CreateVersion7()).Value, Position);
        var otherKey = Protector(OtherKey).Protect(Account, Position);
        var oneBitOff = valid[..^1] + (valid[^1] == 'A' ? 'B' : 'A');
        var cursors = new[]
        {
            oneBitOff, otherAccount, otherKey, valid + "=", valid + valid, new string('A', 65), valid[..43], string.Empty
        };

        foreach (var cursor in cursors)
        {
            using var response = await client.StatementAsync(Account, $"cursor={Uri.EscapeDataString(cursor)}");

            response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
            response.Errors().ShouldHaveSingleItem().ShouldBe(("cursor", "INVALID_CURSOR", CursorMessage));
        }
    }

    [Fact]
    public async Task Statement_WithARepeatedCursor_ReportsInvalidCursor()
    {
        using var client = ReadApiClient.For(factory);
        var valid = Protector(TestConfiguration.CursorSigningKey).Protect(Account, Position);

        using var response = await client.StatementAsync(Account, $"cursor={valid}&cursor={valid}");

        response.Errors().ShouldHaveSingleItem().ShouldBe(("cursor", "INVALID_CURSOR", CursorMessage));
    }

    [Theory]
    [InlineData("balance", "foo=1", "foo")]
    [InlineData("balance", "asof=2026-10-01T10:00:00Z", "asof")]
    [InlineData("balance", "from=2026-10-01T10:00:00Z", "from")]
    [InlineData("balance", "limit=5", "limit")]
    [InlineData("entries", "asOf=2026-10-01T10:00:00Z", "asOf")]
    [InlineData("entries", "foo=1", "foo")]
    [InlineData("entries", "FROM=2026-10-01T10:00:00Z", "FROM")]
    public async Task Reads_WithAnUnknownParameter_ReportUnknownField(string route, string query, string field)
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.GetAsync($"/v1/accounts/{Account}/{route}?{query}");

        response.ShouldBeProblem(HttpStatusCode.BadRequest, "VALIDATION_FAILED", "Falha na validação da requisição");
        response.Errors().ShouldHaveSingleItem().ShouldBe((field, "UNKNOWN_FIELD", "O parâmetro não é suportado."));
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("entries")]
    public async Task Reads_WithAnUnknownParameterOutsideTheSafeAlphabet_ReportItAsUnknown(string route)
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.GetAsync($"/v1/accounts/{Account}/{route}?a%0Ab=1");

        response.Errors().ShouldHaveSingleItem().Field.ShouldBe("(unknown)");
    }

    [Fact]
    public async Task Statement_NeverEchoesAnyValueReceived()
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.StatementAsync(
            Account,
            "from=SENTINEL-FROM&to=SENTINEL-TO&limit=SENTINEL-LIMIT&cursor=SENTINEL-CURSOR&sentinelname=SENTINEL-OTHER");

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
        response.Errors().Count.ShouldBe(5);
        response.Body.ShouldNotContain("SENTINEL", Case.Sensitive);
    }

    private static HmacStatementCursorProtector Protector(string base64Key) =>
        HmacStatementCursorProtector.Create(Convert.FromBase64String(base64Key));
}
