using System.Net;
using System.Net.Http.Headers;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class ScopeAuthorizationHttpTests : IDisposable
{
    private const string ProvisioningClient = "billing-core";

    private readonly TestApiFactory _factory = TestApiFactory.With(new Dictionary<string, string?>
    {
        ["Authorization:AccountProvisioningClients:0"] = ProvisioningClient
    });

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("ledger.reader")]
    [InlineData("LEDGER.READ")]
    [InlineData("ledger.read2")]
    [InlineData("ledger.rea")]
    [InlineData("")]
    public async Task ALookAlikeOrEmptyScope_DoesNotOpenTheReadRoute(string scope)
    {
        using var client = _factory.ClientWith(TokenForge.Hmac(scope: scope));

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("ledger.read")]
    [InlineData("ledger.read ledger.write")]
    [InlineData("ledger.write ledger.read")]
    [InlineData("other.scope ledger.read")]
    public async Task TheReadScope_AloneOrAmongOthers_OpensTheReadRoute(string scope)
    {
        using var client = _factory.ClientWith(TokenForge.Hmac(scope: scope));

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AReadOnlyToken_OnAWriteRoute_GetsForbiddenWithTheRequiredScopeInTheChallenge()
    {
        using var client = _factory.ClientWith(TokenForge.Hmac(scope: "ledger.read"));

        using var response = await Post(client, TestEndpointsStartupFilter.Write);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ChallengeOf(response).ShouldBe("Bearer error=\"insufficient_scope\", scope=\"ledger.write\"");
        await AssertForbiddenProblemAsync(response);
    }

    [Fact]
    public async Task AWriteOnlyToken_OnAReadRoute_GetsForbiddenWithTheRequiredScopeInTheChallenge()
    {
        using var client = _factory.ClientWith(TokenForge.Hmac(scope: "ledger.write"));

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ChallengeOf(response).ShouldBe("Bearer error=\"insufficient_scope\", scope=\"ledger.read\"");
    }

    [Fact]
    public async Task ATokenWithoutClientId_GetsForbiddenWithoutAScopeInTheChallenge()
    {
        using var client = _factory.ClientWith(TokenForge.Hmac(clientId: null));

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ChallengeOf(response).ShouldBe("Bearer error=\"insufficient_scope\"");
    }

    [Fact]
    public async Task ATokenWithoutClientId_IsForbiddenOnTheClosedRouteToo()
    {
        using var client = _factory.ClientWith(TokenForge.Hmac(clientId: null));

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AClientOutsideTheProvisioningList_GetsForbiddenWithoutAScopeInTheChallenge()
    {
        using var client = _factory.ClientWith(TokenForge.Hmac(scope: "ledger.write", clientId: "pix-core"));

        using var response = await Post(client, TestEndpointsStartupFilter.Provisioning);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ChallengeOf(response).ShouldBe("Bearer error=\"insufficient_scope\"");
    }

    [Fact]
    public async Task AClientOnTheProvisioningList_WithTheWriteScope_IsServed()
    {
        using var client = _factory.ClientWith(TokenForge.Hmac(scope: "ledger.write", clientId: ProvisioningClient));

        using var response = await Post(client, TestEndpointsStartupFilter.Provisioning);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AClientOnTheProvisioningList_WithoutTheWriteScope_GetsTheWriteScopeInTheChallenge()
    {
        using var client = _factory.ClientWith(TokenForge.Hmac(scope: "ledger.read", clientId: ProvisioningClient));

        using var response = await Post(client, TestEndpointsStartupFilter.Provisioning);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ChallengeOf(response).ShouldBe("Bearer error=\"insufficient_scope\", scope=\"ledger.write\"");
    }

    private static string ChallengeOf(HttpResponseMessage response) =>
        response.Headers.GetValues("WWW-Authenticate").ShouldHaveSingleItem();

    private static async Task<HttpResponseMessage> Post(HttpClient client, string route)
    {
        using var body = new StringContent("{}");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return await client.PostAsync(route, body, CancellationToken.None);
    }

    private static async Task AssertForbiddenProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        body.ShouldContain("\"code\":\"FORBIDDEN\"");
        body.ShouldNotContain("ledger.write");
    }
}
