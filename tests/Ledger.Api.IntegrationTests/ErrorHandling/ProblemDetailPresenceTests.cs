using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Security;

namespace Ledger.Api.IntegrationTests.ErrorHandling;

[Trait("Category", "Integration")]
public sealed class ProblemDetailPresenceTests(DefaultTestApiFactory factory) : IClassFixture<DefaultTestApiFactory>
{
    [Fact]
    public async Task AnUnhandledException_Returns500WithADetailAndNothingOfTheException()
    {
        using var client = factory.ClientWith(TokenForge.Hmac());

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Boom, CancellationToken.None);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        AssertProblem(body, "INTERNAL_ERROR", 500);
        body.ShouldNotContain("secret detail");
        body.ShouldNotContain("InvalidOperationException");
    }

    [Fact]
    public async Task ATransientFailure_Returns503WithADetailARetryAfterAndNothingOfTheException()
    {
        using var client = factory.ClientWith(TokenForge.Hmac());

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Transient, CancellationToken.None);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(1));
        AssertProblem(body, "SERVICE_UNAVAILABLE", 503);
        body.ShouldNotContain("db.internal");
    }

    [Fact]
    public async Task AnUnknownRoute_Returns404WithADetail()
    {
        using var client = factory.ClientWith(TokenForge.Hmac());

        using var response = await client.GetAsync("/__test/nothing-here", CancellationToken.None);

        AssertProblem(await response.Content.ReadAsStringAsync(CancellationToken.None), "NOT_FOUND", 404);
    }

    [Fact]
    public async Task AWrongMethod_Returns405WithADetailAndTheAllowHeader()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/health/ready", content: null, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.ShouldContain("GET");
        AssertProblem(await response.Content.ReadAsStringAsync(CancellationToken.None), "METHOD_NOT_ALLOWED", 405);
    }

    [Fact]
    public async Task AMissingToken_Returns401WithADetail()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        AssertProblem(await response.Content.ReadAsStringAsync(CancellationToken.None), "UNAUTHENTICATED", 401);
    }

    [Fact]
    public async Task AMissingScope_Returns403WithADetail()
    {
        using var client = factory.ClientWith(TokenForge.Hmac(scope: "ledger.read"));
        using var body = new StringContent("{}");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.PostAsync(TestEndpointsStartupFilter.Write, body, CancellationToken.None);

        AssertProblem(await response.Content.ReadAsStringAsync(CancellationToken.None), "FORBIDDEN", 403);
    }

    [Fact]
    public async Task ARejectedRateLimit_Returns429WithADetail()
    {
        using var limited = TestApiFactory.With(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:ReplenishmentSeconds"] = "3600",
            ["RateLimiting:ReadPerClient:Capacity"] = "1",
            ["RateLimiting:ReadPerClient:RefillPerSecond"] = "1"
        });
        using var client = limited.ClientWith(TokenForge.Hmac());
        using var first = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        using var second = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        AssertProblem(await second.Content.ReadAsStringAsync(CancellationToken.None), "RATE_LIMITED", 429);
    }

    [Fact]
    public async Task AnInvalidBody_Returns400WithADetail()
    {
        using var client = factory.CreateClient();
        using var body = new StringContent("{\"amount\":\"not a number\"}");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.PostAsync(TestEndpointsStartupFilter.BadBody, body, CancellationToken.None);

        var text = await response.Content.ReadAsStringAsync(CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        AssertProblem(text, "VALIDATION_FAILED", 400);
        text.ShouldNotContain("not a number");
    }

    [Fact]
    public async Task AWrongContentType_Returns415WithADetail()
    {
        using var client = factory.ClientWith(TokenForge.Hmac());
        using var body = new StringContent("amount=1");
        body.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var response = await client.PostAsync(TestEndpointsStartupFilter.BadBody, body, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        AssertProblem(await response.Content.ReadAsStringAsync(CancellationToken.None), "UNSUPPORTED_MEDIA_TYPE", 415);
    }

    [Fact]
    public async Task TheCorrelationId_OfTheExceptionPath_IsTheOneOfTheHeader()
    {
        using var client = factory.ClientWith(TokenForge.Hmac());
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpointsStartupFilter.Boom);
        request.Headers.Add("X-Correlation-Id", "caller-supplied-0001");

        using var response = await client.SendAsync(request, CancellationToken.None);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

        problem.RootElement.GetProperty("correlationId").GetString().ShouldBe("caller-supplied-0001");
        response.Headers.GetValues("X-Correlation-Id").ShouldHaveSingleItem().ShouldBe("caller-supplied-0001");
    }

    private static void AssertProblem(string body, string code, int status)
    {
        using var problem = JsonDocument.Parse(body);
        var root = problem.RootElement;

        root.GetProperty("code").GetString().ShouldBe(code);
        root.GetProperty("status").GetInt32().ShouldBe(status);
        root.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        root.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        root.GetProperty("type").GetString().ShouldStartWith("https://ledger.bank.internal/problems/");
        root.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();
        root.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        (root.GetProperty("detail").GetString() ?? string.Empty).ShouldNotContain("Exception");
    }
}
