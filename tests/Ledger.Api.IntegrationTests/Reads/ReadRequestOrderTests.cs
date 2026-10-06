using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Reads;

[Trait("Category", "Integration")]
public sealed class ReadRequestOrderTests(LedgerApiFactory factory) : IClassFixture<LedgerApiFactory>
{
    private static readonly AccountId Account = AccountId.From(Guid.CreateVersion7()).Value;

    [Theory]
    [InlineData("not-a-guid", "balance", "asOf=abc")]
    [InlineData("not-a-guid", "entries", "limit=abc&cursor=lixo")]
    [InlineData("0192b7c281aa7e04b1d56f0c2a9e8d33", "balance", "")]
    [InlineData("0192b7c281aa7e04b1d56f0c2a9e8d33", "entries", "")]
    [InlineData("00000000-0000-0000-0000-000000000000", "balance", "")]
    [InlineData("00000000-0000-0000-0000-000000000000", "entries", "")]
    [InlineData("12345", "balance", "foo=1")]
    public async Task Reads_WithAnAccountIdThatIsNotAGuid_Return404BeforeLookingAtTheParametersOrTheDatabase(
        string accountId,
        string route,
        string query)
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.GetAsync($"/v1/accounts/{accountId}/{route}{(query.Length == 0 ? string.Empty : "?" + query)}");

        response.ShouldBeProblem(
            HttpStatusCode.NotFound,
            "ACCOUNT_NOT_FOUND",
            "Conta não encontrada",
            $"/v1/accounts/{accountId}/{route}");
        response.ShouldCarryNoInternals();
    }

    [Fact]
    public async Task Balance_WithAnUnknownParameterAndAnInvalidAsOf_ReportsTheUnknownParameterFirst()
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.GetAsync($"{ReadApiClient.BalancePath(Account)}?asOf=abc&foo=1");

        response.ShouldBeProblem(HttpStatusCode.BadRequest, "VALIDATION_FAILED", "Falha na validação da requisição");
        response.Errors().ShouldHaveSingleItem().Field.ShouldBe("foo");
    }

    [Fact]
    public async Task Reads_WithInvalidParameters_AnswerWith400AndNeverWith503()
    {
        using var client = ReadApiClient.For(factory);

        using var balance = await client.BalanceAsOfAsync(Account, "abc");
        using var statement = await client.StatementAsync(Account, "limit=0");

        balance.Status.ShouldBe(HttpStatusCode.BadRequest);
        statement.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reads_WithValidParameters_ReachTheDatabaseAndAnswer503WhenItIsUnreachable()
    {
        using var client = ReadApiClient.For(factory);

        using var balance = await client.BalanceAsync(Account);
        using var statement = await client.StatementAsync(Account);

        balance.ShouldBeProblem(HttpStatusCode.ServiceUnavailable, "SERVICE_UNAVAILABLE", "Serviço temporariamente indisponível");
        statement.ShouldBeProblem(HttpStatusCode.ServiceUnavailable, "SERVICE_UNAVAILABLE", "Serviço temporariamente indisponível");
        balance.Header("Retry-After").ShouldBe("1");
        statement.Header("Retry-After").ShouldBe("1");
    }

    [Fact]
    public async Task Statement_DecodesTheCursorInTheSamePassAsTheOtherParameters()
    {
        using var client = ReadApiClient.For(factory);

        using var response = await client.StatementAsync(Account, "limit=0&cursor=lixo");

        response.Errors().Select(error => error.Field).ShouldBe(["limit", "cursor"]);
    }
}
