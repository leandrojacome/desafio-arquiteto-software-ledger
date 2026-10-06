using System.Net;
using System.Net.Http.Headers;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class SecurityHeadersTests
{
    private const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";
    private const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public static TheoryData<string, HttpStatusCode> Scenarios => new()
    {
        { "ok", HttpStatusCode.OK },
        { "unauthenticated", HttpStatusCode.Unauthorized },
        { "forbidden", HttpStatusCode.Forbidden },
        { "not found", HttpStatusCode.NotFound },
        { "method not allowed", HttpStatusCode.MethodNotAllowed },
        { "unsupported media type", HttpStatusCode.UnsupportedMediaType },
        { "bad request", HttpStatusCode.BadRequest },
        { "internal error", HttpStatusCode.InternalServerError },
        { "service unavailable", HttpStatusCode.ServiceUnavailable }
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task EveryResponse_CarriesTheSecurityHeaders(string scenario, HttpStatusCode expected)
    {
        using var factory = TestApiFactory.With();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, scenario);

        response.StatusCode.ShouldBe(expected, scenario);
        AssertSecurityHeaders(response);
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task ATooManyRequestsResponse_CarriesTheSecurityHeaders()
    {
        using var factory = TestApiFactory.With(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:ReplenishmentSeconds"] = "3600",
            ["RateLimiting:WritePerClient:Capacity"] = "1",
            ["RateLimiting:WritePerClient:RefillPerSecond"] = "1"
        });
        using var client = factory.Authenticated();
        using var first = await PostAsync(client, TestEndpointsStartupFilter.Write);

        using var second = await PostAsync(client, TestEndpointsStartupFilter.Write);

        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        AssertSecurityHeaders(second);
    }

    [Fact]
    public async Task ASaturatedConcurrencyResponse_CarriesTheSecurityHeaders()
    {
        using var gate = new SemaphoreSlim(0);
        using var entered = new SemaphoreSlim(0);
        using var factory = TestApiFactory.With(
            new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "true",
                ["RateLimiting:WriteConcurrency"] = "1",
                ["Postgres:Sources:Write:MinPoolSize"] = "0",
                ["Postgres:Sources:Write:MaxPoolSize"] = "1"
            },
            routes: routes => HeldRequests.Map(routes, entered, gate, anonymous: true));
        using var client = factory.CreateClient();

        await HeldRequests.WhileHeldAsync(client, entered, gate, async () =>
        {
            using var refused = await HeldRequests.PostAsync(client);

            refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            AssertSecurityHeaders(refused);
        });
    }

    [Fact]
    public async Task APayloadTooLargeResponse_CarriesTheSecurityHeaders()
    {
        using var factory = new KestrelApiFactory();
        using var client = factory.CreateKestrelClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenForge.Hmac());
        using var body = new StringContent(new string('x', ApiConstants.MaxRequestBodyBytes + 1));
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.PostAsync(TestEndpointsStartupFilter.BadBody, body, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        AssertSecurityHeaders(response);
    }

    [Theory]
    [InlineData("Testing", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    public async Task TheStrictTransportSecurity_IsSentOutsideDevelopmentOnly(string environment, bool expected)
    {
        using var keys = TemporaryKeyDirectory.WithVersionOne();
        var overrides = environment switch
        {
            "Staging" => ProductionLikeSettings.Create(keys),
            "Development" => new Dictionary<string, string?> { ["Authentication:LocalKey:PublicKeyPath"] = null },
            _ => null
        };
        using var factory = TestApiFactory.With(overrides, environment);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Open, CancellationToken.None);

        response.Headers.TryGetValues("Strict-Transport-Security", out var values).ShouldBe(expected);

        if (expected)
        {
            values.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(StrictTransportSecurity);
        }
    }

    [Fact]
    public async Task TheServerHeader_IsNotSentByTheRealServer()
    {
        using var factory = new KestrelApiFactory();
        using var client = factory.CreateKestrelClient();

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Open, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task APlainHttpRequest_IsNeverRedirectedToHttps()
    {
        using var factory = new KestrelApiFactory();
        using var client = factory.CreateKestrelClient();

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Open, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task ACrossOriginRequestAndItsPreflight_GetNoCorsHeaders()
    {
        using var factory = TestApiFactory.With();
        using var client = factory.CreateClient();
        using var simple = new HttpRequestMessage(HttpMethod.Get, TestEndpointsStartupFilter.Open);
        simple.Headers.Add("Origin", "https://app.bank.example");
        using var preflight = new HttpRequestMessage(HttpMethod.Options, TestEndpointsStartupFilter.Open);
        preflight.Headers.Add("Origin", "https://app.bank.example");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");

        using var simpleResponse = await client.SendAsync(simple, CancellationToken.None);
        using var preflightResponse = await client.SendAsync(preflight, CancellationToken.None);

        simpleResponse.Headers.Any(header => header.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase))
            .ShouldBeFalse();
        preflightResponse.Headers.Any(header => header.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase))
            .ShouldBeFalse();
        preflightResponse.StatusCode.ShouldNotBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task TheRealServerOptions_HideTheServerHeaderAndCapTheBody()
    {
        using var factory = new KestrelApiFactory();
        using var client = factory.CreateKestrelClient();

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Open, CancellationToken.None);

        var options = factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>>()
            .Value;

        options.AddServerHeader.ShouldBeFalse();
        options.Limits.MaxRequestBodySize.ShouldBe(ApiConstants.MaxRequestBodyBytes);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");
        response.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldBe(ContentSecurityPolicy);
        response.Headers.GetValues("Referrer-Policy").ShouldHaveSingleItem().ShouldBe("no-referrer");
        response.Headers.GetValues("Cache-Control").ShouldHaveSingleItem().ShouldBe("no-store");
        response.Headers.Contains("X-Correlation-Id").ShouldBeTrue();
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route)
    {
        using var body = new StringContent("{}");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return await client.PostAsync(route, body, CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string scenario)
    {
        switch (scenario)
        {
            case "ok":
                return await client.GetAsync(TestEndpointsStartupFilter.Open, CancellationToken.None);
            case "unauthenticated":
                return await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);
            case "forbidden":
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", TokenForge.Hmac(scope: "ledger.write"));

                return await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
            case "not found":
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenForge.Hmac());

                return await client.GetAsync("/__test/does-not-exist", CancellationToken.None);
            case "method not allowed":
                return await client.PostAsync("/health/live", content: null, CancellationToken.None);
            case "unsupported media type":
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenForge.Hmac());
                using (var text = new StringContent("amount=1"))
                {
                    text.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

                    return await client.PostAsync(TestEndpointsStartupFilter.BadBody, text, CancellationToken.None);
                }

            case "bad request":
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenForge.Hmac());
                using (var json = new StringContent("{\"amount\":\"not a number\"}"))
                {
                    json.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                    return await client.PostAsync(TestEndpointsStartupFilter.BadBody, json, CancellationToken.None);
                }

            case "internal error":
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenForge.Hmac());

                return await client.GetAsync(TestEndpointsStartupFilter.Boom, CancellationToken.None);
            case "service unavailable":
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenForge.Hmac());

                return await client.GetAsync(TestEndpointsStartupFilter.Transient, CancellationToken.None);
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.");
        }
    }
}
