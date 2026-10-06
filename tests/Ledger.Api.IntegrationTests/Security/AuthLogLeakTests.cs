using System.Net.Http.Headers;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class AuthLogLeakTests
{
    private const string Canary = "canary-body-8f3d2c71-do-not-log-this-text";

    [Fact]
    public async Task AcceptedRequest_LeavesNoTokenOrBodyInAnyLogLine()
    {
        using var factory = TestApiFactory.With();
        var token = TokenForge.Hmac(clientId: "pix-gateway");
        using var client = factory.ClientWith(token);

        using var body = Body();
        using var response = await client.PostAsync(TestEndpointsStartupFilter.Echo, body, CancellationToken.None);

        response.EnsureSuccessStatusCode();
        AssertClean(factory, token);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("other-key")]
    [InlineData("malformed")]
    [InlineData("alg-none")]
    public async Task RejectedRequest_LeavesNoTokenOrBodyInAnyLogLine(string kind)
    {
        using var factory = TestApiFactory.With();
        var token = kind switch
        {
            "expired" => TokenForge.Hmac(expiresIn: TimeSpan.FromSeconds(-40)),
            "other-key" => TokenForge.Hmac(secret: "a-different-signing-key-with-more-than-32-chars"),
            "alg-none" => TokenForge.Unsigned(),
            _ => "eyJhbGciOiJIUzI1NiJ9.this-is-not-a-real.payload"
        };
        using var client = factory.ClientWith(token);

        using var body = Body();
        using var response = await client.PostAsync(TestEndpointsStartupFilter.Write, body, CancellationToken.None);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
        AssertClean(factory, token);
    }

    [Fact]
    public async Task ForbiddenRequest_LeavesNoTokenOrBodyInAnyLogLine()
    {
        using var factory = TestApiFactory.With();
        var token = TokenForge.Hmac(scope: "ledger.read", clientId: "reporting");
        using var client = factory.ClientWith(token);

        using var body = Body();
        using var response = await client.PostAsync(TestEndpointsStartupFilter.Write, body, CancellationToken.None);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
        AssertClean(factory, token);
    }

    private static StringContent Body()
    {
        var body = new StringContent("{\"note\":\"" + Canary + "\"}");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return body;
    }

    private static void AssertClean(TestApiFactory factory, string token)
    {
        var everything = factory.Sink.Everything();

        everything.ShouldNotContain(token);
        everything.ShouldNotContain(token[..Math.Min(40, token.Length)]);
        everything.ShouldNotContain(token[^Math.Min(20, token.Length)..]);
        everything.ShouldNotContain("Bearer ey");
        everything.ShouldNotContain(Canary);
    }
}
