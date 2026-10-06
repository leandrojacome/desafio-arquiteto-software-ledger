using System.Net;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Health;

[Trait("Category", "Integration")]
public sealed class TestEndpointsStartupFilterTests(DefaultTestApiFactory factory) : IClassFixture<DefaultTestApiFactory>
{
    [Fact]
    public async Task TheOpenRoute_AnswersWithoutAToken()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Open, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheClosedRoute_DeniesAnonymousCallersAndServesAuthenticatedOnes()
    {
        using var anonymous = factory.CreateClient();
        using var denied = await anonymous.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        using var authenticated = factory.Authenticated();
        using var served = await authenticated.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnExceptionFromATestRoute_ReachesTheGlobalHandlerAndComesBackAsAProblem()
    {
        using var client = factory.Authenticated();

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Boom, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.Contains("X-Correlation-Id").ShouldBeTrue();

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

        problem.RootElement.GetProperty("code").GetString().ShouldBe("INTERNAL_ERROR");
    }
}
