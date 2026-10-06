using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Accounts;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class CreateAccountValidationTests(PostgresFixture postgres)
{
    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task EveryProblem_ComesInOneBadRequestWithoutEchoingTheValues()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var before = await _data.CountAccountsAsync();

        var response = await client.PostAccountAsync(
            "{\"holderDocument\":\"987.654.321-01\",\"currency\":\"EUR\",\"overdraftLimit\":\"-5.123\",\"clientId\":\"intruder\"}");

        response.ShouldBeValidationProblem().ShouldBe(
        [
            ("holderDocument", "INVALID_FORMAT"),
            ("currency", "UNSUPPORTED_CURRENCY"),
            ("overdraftLimit", "OUT_OF_RANGE"),
            ("clientId", "UNKNOWN_FIELD")
        ]);
        var texts = response.DescriptiveTexts();

        texts.ShouldNotBeEmpty();
        texts.ShouldAllBe(text => !text.Contains("987", StringComparison.Ordinal));
        texts.ShouldAllBe(text => !text.Contains("EUR", StringComparison.Ordinal));
        texts.ShouldAllBe(text => !text.Contains("5.123", StringComparison.Ordinal));
        (await _data.CountAccountsAsync()).ShouldBe(before);
    }

    [DockerTheory]
    [InlineData("{\"currency\":\"BRL\"}", "holderDocument", "REQUIRED")]
    [InlineData("{\"holderDocument\":\"123.456.789-09\"}", "currency", "REQUIRED")]
    [InlineData("{\"holderDocument\":123,\"currency\":\"BRL\"}", "holderDocument", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"123.456.789-00\",\"currency\":\"BRL\"}", "holderDocument", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"11111111111\",\"currency\":\"BRL\"}", "holderDocument", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"123.456.789-09\",\"currency\":\"brl\"}", "currency", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"123.456.789-09\",\"currency\":\"EUR\"}", "currency", "UNSUPPORTED_CURRENCY")]
    [InlineData("{\"holderDocument\":\"123.456.789-09\",\"currency\":\"BRL\",\"overdraftLimit\":5}", "overdraftLimit", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"123.456.789-09\",\"currency\":\"BRL\",\"overdraftLimit\":\"1000000000.00\"}", "overdraftLimit", "OUT_OF_RANGE")]
    [InlineData("{\"holderDocument\":\"123.456.789-09\",\"currency\":\"BRL\",\"overdraftLimit\":\"1.001\"}", "overdraftLimit", "TOO_MANY_DECIMALS")]
    [InlineData("", "$", "INVALID_JSON")]
    [InlineData("[]", "$", "INVALID_JSON")]
    [InlineData("{\"currency\":\"BRL\",\"currency\":\"BRL\"}", "$", "INVALID_JSON")]
    public async Task EachCase_IsReportedWithItsFieldAndReason(string body, string field, string reason)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostAccountAsync(body);

        response.ShouldBeValidationProblem().ShouldBe([(field, reason)]);
    }

    [DockerFact]
    public async Task ValidationFailure_UsesTheCatalogTitleAndTheFixedDetail()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostAccountAsync("{}");

        var problem = response.ShouldBeProblem(400, "VALIDATION_FAILED");
        problem.GetProperty("title").GetString().ShouldBe("Falha na validação da requisição");
        problem.GetProperty("detail").GetString().ShouldBe("Um ou mais campos são inválidos.");
        problem.GetProperty("instance").GetString().ShouldBe("/v1/accounts");
    }
}
