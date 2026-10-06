using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Accounts;

[Trait("Category", "Integration")]
public sealed class CreateAccountEmptyKeyTests
{
    [Fact]
    public async Task AnEmptyKeyHeader_IsABadRequestInsteadOfBeingTreatedAsAbsent()
    {
        using var factory = new KestrelApiFactory();
        var client = new WriteClient(factory.CreateKestrelClient());

        var response = await client.PostAccountAsync(options: new WriteRequestOptions { IdempotencyKey = string.Empty });

        response.ShouldBeValidationProblem().ShouldBe([("Idempotency-Key", "INVALID_FORMAT")]);
    }
}
