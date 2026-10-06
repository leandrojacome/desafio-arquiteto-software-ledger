using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Health;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class ReadinessWithDatabaseTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task Ready_WhenTheDatabaseIsUp_ReturnsOk()
    {
        await using var factory = new DatabaseLedgerApiFactory(postgres);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
