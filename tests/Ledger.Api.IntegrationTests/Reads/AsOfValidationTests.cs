using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Reads;

[Trait("Category", "Integration")]
public sealed class AsOfValidationTests(LedgerApiFactory factory) : IClassFixture<LedgerApiFactory>
{
    private const string Title = "Instante 'asOf' inválido";
    private const string Detail =
        "O parâmetro 'asOf' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo.";

    private static readonly AccountId Account = AccountId.From(Guid.CreateVersion7()).Value;

    [Theory]
    [InlineData("2026-10-01T14:03:11")]
    [InlineData("2026-10-01T14:03:11.5")]
    [InlineData("2026-10-01T14:03:11+25:00")]
    [InlineData("2026-10-01T11:03:11-15:00")]
    [InlineData("2026-10-01T14:03:11.Z")]
    [InlineData("2026-10-01T14:03:11+0000")]
    [InlineData("2026-10-01T14:03:11-00:00")]
    [InlineData("2026-10-01T14:03:11.4829131Z")]
    [InlineData("2026-10-01")]
    [InlineData("2026-10-01T24:00:00Z")]
    [InlineData("2026-02-30T00:00:00Z")]
    [InlineData("٢٠٢٦-10-01T14:03:11Z")]
    [InlineData("2026-10-01T14:03:11Z\n")]
    [InlineData(" 2026-10-01T14:03:11Z")]
    [InlineData("")]
    [InlineData("abc")]
    public async Task Balance_WithAnAsOfOutsideTheStrictGrammar_Returns400InvalidAsOfAndNeverTouchesTheDatabase(string asOf)
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.BalanceAsOfAsync(Account, asOf);

        response.ShouldBeProblem(HttpStatusCode.BadRequest, "INVALID_AS_OF", Title, ReadApiClient.BalancePath(Account));
        response.Text("detail").ShouldBe(Detail);
        response.Has("errors").ShouldBeFalse();
        response.Header("Cache-Control").ShouldBe("no-store");
        response.ShouldCarryNoInternals();
    }

    [Fact]
    public async Task Balance_WithARepeatedAsOf_Returns400InvalidAsOf()
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.GetAsync(
            $"{ReadApiClient.BalancePath(Account)}?asOf=2026-10-01T14:03:11Z&asOf=2026-10-01T14:03:12Z");

        response.ShouldBeProblem(HttpStatusCode.BadRequest, "INVALID_AS_OF", Title);
        response.Has("errors").ShouldBeFalse();
    }

    [Fact]
    public async Task Balance_WithAnInvalidAsOf_NeverEchoesTheValueReceived()
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.BalanceAsOfAsync(Account, "SENTINEL-AS-OF-7731");

        response.Status.ShouldBe(HttpStatusCode.BadRequest);
        response.Body.ShouldNotContain("SENTINEL");
    }

    [Fact]
    public async Task Balance_WithAnUnknownParameterAndAnInvalidAsOf_ReportsOnlyTheUnknownParameter()
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.GetAsync($"{ReadApiClient.BalancePath(Account)}?foo=1&asOf=abc");

        response.ShouldBeProblem(HttpStatusCode.BadRequest, "VALIDATION_FAILED", "Falha na validação da requisição");
        response.Errors().ShouldHaveSingleItem().ShouldBe(("foo", "UNKNOWN_FIELD", "O parâmetro não é suportado."));
    }
}
