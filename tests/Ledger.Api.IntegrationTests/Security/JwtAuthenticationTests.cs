using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class JwtAuthenticationTests(DefaultTestApiFactory factory) : IClassFixture<DefaultTestApiFactory>
{
    private const string ProtectedRoute = TestEndpointsStartupFilter.Closed;

    [Fact]
    public async Task Request_WithoutToken_ReturnsUnauthorizedWithBearerChallenge()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(ProtectedRoute, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ShouldContain(header => header.Scheme == "Bearer");
    }

    [Fact]
    public async Task Request_WithValidToken_PassesAuthenticationAndReachesTheEndpoint()
    {
        using var client = factory.Authenticated();

        using var response = await client.GetAsync(ProtectedRoute, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_WithTokenForAnotherAudience_ReturnsUnauthorized()
    {
        await AssertRejectedAsync(TestTokenFactory.Create(audience: "another-api"));
    }

    [Fact]
    public async Task Request_WithTokenFromAnotherIssuer_ReturnsUnauthorized()
    {
        await AssertRejectedAsync(TestTokenFactory.Create(issuer: "https://untrusted.example"));
    }

    [Fact]
    public async Task Request_WithExpiredToken_ReturnsUnauthorized()
    {
        await AssertRejectedAsync(TestTokenFactory.Create(lifetime: TimeSpan.FromHours(-1)));
    }

    [Fact]
    public async Task Request_WithTokenSignedByAnotherKey_ReturnsUnauthorized()
    {
        await AssertRejectedAsync(
            TestTokenFactory.Create(signingKey: "a-different-signing-key-with-more-than-32-chars"));
    }

    [Fact]
    public async Task Request_WithUnsignedToken_ReturnsUnauthorized()
    {
        await AssertRejectedAsync(TestTokenFactory.Create(withoutSignature: true));
    }

    [Fact]
    public async Task Request_WithTokenThatLacksClientId_ReturnsForbidden()
    {
        using var client = factory.Authenticated(TestTokenFactory.Create(clientId: null));

        using var response = await client.GetAsync(ProtectedRoute, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HealthEndpoints_DoNotRequireAToken()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task AssertRejectedAsync(string token)
    {
        using var client = factory.Authenticated(token);

        using var response = await client.GetAsync(ProtectedRoute, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
