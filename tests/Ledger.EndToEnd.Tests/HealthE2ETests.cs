using System.Net;

namespace Ledger.EndToEnd.Tests;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class HealthE2ETests(E2EFixture stack)
{
    [E2EFact]
    public async Task Live_ReturnsOk()
    {
        using var client = stack.CreateClient();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [E2EFact]
    public async Task Ready_ReturnsOkWhenTheStackIsUp()
    {
        using var client = stack.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
