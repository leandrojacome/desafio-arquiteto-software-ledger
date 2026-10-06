using System.Net;
using System.Net.Http.Headers;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class PipelineOrderTests
{
    private const string Correlation = "pipeline-order-0001";

    [Fact]
    public async Task TheCorrelationId_IsKnownBeforeTheAuthenticationRejectsTheRequest()
    {
        using var factory = TestApiFactory.With();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpointsStartupFilter.Closed);
        request.Headers.Add("X-Correlation-Id", Correlation);

        using var response = await client.SendAsync(request, CancellationToken.None);

        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.GetValues("X-Correlation-Id").ShouldHaveSingleItem().ShouldBe(Correlation);
        problem.RootElement.GetProperty("correlationId").GetString().ShouldBe(Correlation);
    }

    [Fact]
    public async Task TheQuota_IsKeyedByTheAuthenticatedClientBecauseAuthenticationRunsBeforeTheLimiter()
    {
        using var factory = TestApiFactory.With(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:ReplenishmentSeconds"] = "3600",
            ["RateLimiting:ReadPerClient:Capacity"] = "1",
            ["RateLimiting:ReadPerClient:RefillPerSecond"] = "1"
        });
        using var first = factory.ClientWith(TokenForge.Hmac(clientId: "pix-gateway"));
        using var second = factory.ClientWith(TokenForge.Hmac(clientId: "cards-core"));

        using var firstAccepted = await first.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var secondAccepted = await second.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var firstRefused = await first.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        firstAccepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondAccepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        firstRefused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheLimiter_AnswersBeforeTheAuthorizationDeniesTheScope()
    {
        using var factory = TestApiFactory.With(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:ReplenishmentSeconds"] = "3600",
            ["RateLimiting:ReadPerClient:Capacity"] = "1",
            ["RateLimiting:ReadPerClient:RefillPerSecond"] = "1"
        });
        using var client = factory.ClientWith(TokenForge.Hmac(scope: "ledger.write"));

        using var denied = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var limited = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheAuthentication_RunsBeforeTheLimiterSoABadTokenIsRejectedWithoutSpendingTheAnonymousQuota()
    {
        using var factory = TestApiFactory.With(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:ReplenishmentSeconds"] = "3600",
            ["RateLimiting:ReadPerClient:Capacity"] = "2",
            ["RateLimiting:ReadPerClient:RefillPerSecond"] = "1"
        });
        using var client = factory.ClientWith("not-a-jwt");

        using var first = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var second = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var third = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        first.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        second.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheTimeoutResponse_StillCarriesTheHeadersOfTheOuterMiddlewares()
    {
        using var factory = TestApiFactory.With(new Dictionary<string, string?> { ["Resilience:RequestTimeoutSeconds"] = "1" });
        using var client = factory.ClientWith(TokenForge.Hmac());

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Timeout, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.Contains("X-Correlation-Id").ShouldBeTrue();
        response.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task TheStatusPages_GiveTheRejectionsOfTheInnerMiddlewaresAProblemBody()
    {
        using var factory = TestApiFactory.With();
        using var anonymous = factory.CreateClient();
        using var unscoped = factory.ClientWith(TokenForge.Hmac(scope: "ledger.read"));
        using var body = new StringContent("{}");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var unauthenticated = await anonymous.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);
        using var forbidden = await unscoped.PostAsync(TestEndpointsStartupFilter.Write, body, CancellationToken.None);

        unauthenticated.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");
        forbidden.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");
    }
}
