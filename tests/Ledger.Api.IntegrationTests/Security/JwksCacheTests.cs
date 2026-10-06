using System.Net;
using System.Security.Cryptography;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Security;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Security")]
[Trait("Category", "Resilience")]
[Trait("Category", "Integration")]
public sealed class JwksCacheTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task WithTheIssuerDown_TokensOfAKnownKeyKeepWorking_AnUnknownKeyIsRefused_AndReadinessDoesNotChange()
    {
        await using var issuer = await FakeIssuer.StartAsync();
        using var factory = TestApiFactory.With(postgres.ConfigurationWith(AuthorityMode(issuer)));
        using var known = factory.ClientWith(KnownKeyToken(issuer));

        using var firstCall = await known.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);
        using var readyBefore = await known.GetAsync("/health/ready", CancellationToken.None);

        firstCall.StatusCode.ShouldBe(HttpStatusCode.OK);
        readyBefore.StatusCode.ShouldBe(HttpStatusCode.OK);
        issuer.KeyRequests.ShouldBe(1);

        await issuer.StopAsync();

        using var cached = factory.ClientWith(KnownKeyToken(issuer));
        using var stranger = factory.ClientWith(UnknownKeyToken(issuer));

        using var knownAfter = await cached.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);
        using var unknownAfter = await stranger.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);
        using var readyAfter = await cached.GetAsync("/health/ready", CancellationToken.None);
        using var liveAfter = await cached.GetAsync("/health/live", CancellationToken.None);

        knownAfter.StatusCode.ShouldBe(HttpStatusCode.OK, "a token of a key already in the cache must keep working");
        unknownAfter.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "an unknown key id cannot be resolved without the issuer");
        unknownAfter.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer error=\"invalid_token\"");
        readyAfter.StatusCode.ShouldBe(HttpStatusCode.OK, "readiness never asks the issuer");
        liveAfter.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [DockerFact]
    public async Task TheKeysAreFetchedOnceAndServedFromTheCacheForTheFollowingRequests()
    {
        await using var issuer = await FakeIssuer.StartAsync();
        using var factory = TestApiFactory.With(postgres.ConfigurationWith(AuthorityMode(issuer)));
        using var client = factory.ClientWith(KnownKeyToken(issuer));

        for (var call = 0; call < 5; call++)
        {
            using var response = await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        issuer.DiscoveryRequests.ShouldBe(1);
        issuer.KeyRequests.ShouldBe(1);
    }

    [DockerFact]
    public async Task ATokenSignedByAKeyTheIssuerNeverPublished_IsRefusedEvenWithTheIssuerUp()
    {
        await using var issuer = await FakeIssuer.StartAsync();
        using var factory = TestApiFactory.With(postgres.ConfigurationWith(AuthorityMode(issuer)));
        using var client = factory.ClientWith(UnknownKeyToken(issuer));

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static Dictionary<string, string?> AuthorityMode(FakeIssuer issuer)
    {
        return new Dictionary<string, string?>
        {
            ["Authentication:Mode"] = "Authority",
            ["Authentication:Authority"] = issuer.Address,
            ["Authentication:Issuer"] = issuer.Address,
            ["Authentication:RequireHttpsMetadata"] = "false"
        };
    }

    private static string KnownKeyToken(FakeIssuer issuer) =>
        TokenForge.Create(issuer.Credentials(), issuer: issuer.Address);

    private static string UnknownKeyToken(FakeIssuer issuer)
    {
        using var stranger = RSA.Create(2048);

        return TokenForge.Create(TokenForge.Rsa(stranger, "key-nobody-published"), issuer: issuer.Address);
    }
}
