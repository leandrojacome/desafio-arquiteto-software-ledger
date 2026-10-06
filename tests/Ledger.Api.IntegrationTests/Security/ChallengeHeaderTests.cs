using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class ChallengeHeaderTests(DefaultTestApiFactory factory) : IClassFixture<DefaultTestApiFactory>
{
    private const string Challenge = "Bearer";
    private const string InvalidTokenChallenge = "Bearer error=\"invalid_token\"";

    private static readonly Dictionary<string, Func<string>> RejectedTokenFactories = new()
    {
        ["malformed"] = () => "this-is-not-a-jwt",
        ["other signing key"] = () => TokenForge.Hmac(secret: "a-different-signing-key-with-more-than-32-chars"),
        ["other audience"] = () => TokenForge.Hmac(audience: "another-api"),
        ["other issuer"] = () => TokenForge.Hmac(issuer: "https://untrusted.example"),
        ["no expiration"] = () => TokenForge.Hmac(withoutExpiration: true),
        ["alg none"] = () => TokenForge.Unsigned(),
        ["expired 40 seconds ago"] = () => TokenForge.Hmac(expiresIn: TimeSpan.FromSeconds(-40)),
        ["not valid yet by 40 seconds"] = () => TokenForge.Hmac(notBeforeIn: TimeSpan.FromSeconds(40)),
        ["another token type"] = () => TokenForge.Hmac(tokenType: "JWT"),
        ["lifetime above the ceiling"] = () => TokenForge.Hmac(expiresIn: TimeSpan.FromHours(2))
    };

    public static TheoryData<string> RejectedTokens
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var description in RejectedTokenFactories.Keys)
            {
                data.Add(description);
            }

            return data;
        }
    }

    [Fact]
    public async Task WithoutTheAuthorizationHeader_TheChallengeIsBearerAlone()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ChallengeOf(response).ShouldBe(Challenge);
        await AssertProblemAsync(response);
    }

    [Fact]
    public async Task WithTheBasicScheme_TheChallengeIsBearerAlone()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "dXNlcjpwYXNz");

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ChallengeOf(response).ShouldBe(Challenge);
    }

    [Theory]
    [MemberData(nameof(RejectedTokens))]
    public async Task ARejectedToken_GetsInvalidTokenWithoutAnyExplanation(string description)
    {
        var token = RejectedTokenFactories[description]();
        using var client = factory.ClientWith(token);

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, description);
        ChallengeOf(response).ShouldBe(InvalidTokenChallenge, description);
        response.Headers.WwwAuthenticate.ToString().ShouldNotContain("error_description");

        var body = await AssertProblemAsync(response);

        body.ShouldNotContain(token);
        body.ShouldNotContain(TestConfiguration.Issuer);
        body.ShouldNotContain("expired");
    }

    [Fact]
    public async Task TheProblemBody_NamesTheCodeAndCarriesTheCorrelationAndTheTrace()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));
        var root = problem.RootElement;

        root.GetProperty("code").GetString().ShouldBe("UNAUTHENTICATED");
        root.GetProperty("status").GetInt32().ShouldBe(401);
        root.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        root.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        root.GetProperty("correlationId").GetString()
            .ShouldBe(response.Headers.GetValues("X-Correlation-Id").ShouldHaveSingleItem());
    }

    private static string ChallengeOf(HttpResponseMessage response) =>
        response.Headers.GetValues("WWW-Authenticate").ShouldHaveSingleItem();

    private static async Task<string> AssertProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");
        response.Headers.Contains("X-Correlation-Id").ShouldBeTrue();

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var problem = JsonDocument.Parse(body);

        problem.RootElement.GetProperty("code").GetString().ShouldBe("UNAUTHENTICATED");
        problem.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
        problem.RootElement.TryGetProperty("correlationId", out _).ShouldBeTrue();

        return body;
    }
}
