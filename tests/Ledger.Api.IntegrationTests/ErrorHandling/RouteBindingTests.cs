using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.ErrorHandling;

[Trait("Category", "Integration")]
public sealed class RouteBindingTests
{
    private const string RejectionLog = "The request was rejected by the server";

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void BindingFailures_ThrowInEveryNonStrictEnvironment(string environment)
    {
        using var factory = TestApiFactory.With(
            new Dictionary<string, string?> { ["Authentication:LocalKey:PublicKeyPath"] = null },
            environment);

        factory.Services.GetRequiredService<IOptions<RouteHandlerOptions>>().Value.ThrowOnBadRequest.ShouldBeTrue();
    }

    [Fact]
    public void BindingFailures_ThrowInAProductionLikeEnvironmentToo()
    {
        using var keys = TemporaryKeyDirectory.WithVersionOne();
        using var factory = TestApiFactory.With(ProductionLikeSettings.Create(keys), "Production");

        factory.Services.GetRequiredService<IOptions<RouteHandlerOptions>>().Value.ThrowOnBadRequest.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Production")]
    public async Task AnInvalidBinding_ReachesTheGlobalHandlerAndComesBackAsAValidationProblem(string environment)
    {
        using var keys = TemporaryKeyDirectory.WithVersionOne();
        using var factory = TestApiFactory.With(
            environment == "Production" ? ProductionLikeSettings.Create(keys) : null,
            environment);
        using var client = factory.CreateClient();
        using var body = Json("{\"amount\":\"not a number\"}");

        using var response = await client.PostAsync(TestEndpointsStartupFilter.BadBody, body, CancellationToken.None);

        var text = await response.Content.ReadAsStringAsync(CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");
        using var problem = JsonDocument.Parse(text);

        problem.RootElement.GetProperty("code").GetString().ShouldBe("VALIDATION_FAILED");
        problem.RootElement.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.RootElement.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();
        text.ShouldNotContain("not a number");
        RejectionLogged(factory).ShouldBeTrue();
    }

    [Fact]
    public async Task AnUnknownPropertyInTheBody_IsRefusedAsAValidationProblem()
    {
        using var factory = TestApiFactory.With();
        using var client = factory.CreateClient();
        using var body = Json("{\"amount\":1,\"extra\":true}");

        using var response = await client.PostAsync(TestEndpointsStartupFilter.BadBody, body, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        RejectionLogged(factory).ShouldBeTrue();
    }

    [Fact]
    public async Task AMalformedJsonBody_IsRefusedAsAValidationProblem()
    {
        using var factory = TestApiFactory.With();
        using var client = factory.CreateClient();
        using var body = Json("{\"amount\":");

        using var response = await client.PostAsync(TestEndpointsStartupFilter.BadBody, body, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        RejectionLogged(factory).ShouldBeTrue();
    }

    [Fact]
    public async Task AWrongContentType_IsRefusedAs415AsAProblem()
    {
        using var factory = TestApiFactory.With();
        using var client = factory.ClientWith(TokenForge.Hmac());
        using var body = new StringContent("amount=1");
        body.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var response = await client.PostAsync(TestEndpointsStartupFilter.BadBody, body, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

        problem.RootElement.GetProperty("code").GetString().ShouldBe("UNSUPPORTED_MEDIA_TYPE");
    }

    [Fact]
    public async Task ABodyAboveTheLimit_ReadByTheRoute_IsRefusedAs413ThroughTheGlobalHandlerOnTheRealServer()
    {
        using var factory = new KestrelApiFactory();
        using var client = factory.CreateKestrelClient();
        using var body = Json(new string(' ', ApiConstants.MaxRequestBodyBytes + 1));

        using var response = await client.PostAsync(TestEndpointsStartupFilter.Echo, body, CancellationToken.None);

        var text = await response.Content.ReadAsStringAsync(CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        using var problem = JsonDocument.Parse(text);

        problem.RootElement.GetProperty("code").GetString().ShouldBe("PAYLOAD_TOO_LARGE");
        problem.RootElement.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        RejectionLogged(factory).ShouldBeTrue();
    }

    [Fact]
    public async Task ABodyAboveTheLimit_BoundByTheFramework_IsRefusedAs413OnTheRealServer()
    {
        using var factory = new KestrelApiFactory();
        using var client = factory.CreateKestrelClient();
        using var body = Json(new string(' ', ApiConstants.MaxRequestBodyBytes + 1));

        using var response = await client.PostAsync(TestEndpointsStartupFilter.BadBody, body, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task ABodyAtTheLimit_IsStillRead()
    {
        using var factory = new KestrelApiFactory();
        using var client = factory.CreateKestrelClient();
        var padding = new string(' ', ApiConstants.MaxRequestBodyBytes - "{\"amount\":1}".Length);
        using var body = Json("{\"amount\":1}" + padding);

        using var response = await client.PostAsync(TestEndpointsStartupFilter.BadBody, body, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static StringContent Json(string text)
    {
        var content = new StringContent(text);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return content;
    }

    private static bool RejectionLogged(TestApiFactory factory)
    {
        return factory.Sink.Events.Any(logEvent =>
            logEvent.MessageTemplate.Text.StartsWith(RejectionLog, StringComparison.Ordinal));
    }
}
