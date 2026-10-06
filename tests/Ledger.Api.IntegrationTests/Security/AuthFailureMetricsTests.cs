using System.Net;
using System.Net.Http.Headers;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Observability;
using Ledger.Api.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
[Collection(OtelEnvironment.Collection)]
public sealed class AuthFailureMetricsTests : IDisposable
{
    private const string Account = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33";
    private const string WriteRoute = $"/v1/accounts/{Account}/entries";
    private const string ReadRoute = $"/v1/accounts/{Account}/balance";
    private const string TemplateOfTheWriteRoute = "/v1/accounts/{accountId}/entries";
    private const string TemplateOfTheReadRoute = "/v1/accounts/{accountId}/balance";

    private readonly EnvironmentVariableScope _environment = OtelEnvironment.Clean();
    private readonly LogCapture<AuthenticationEvents> _rejections = new();
    private readonly LogCapture<LedgerAuthorizationResultHandler> _denials = new();
    private readonly TaggedCounter _failures = new("Ledger", "ledger.auth.failures");
    private readonly ObservabilityApiFactory _factory;

    public AuthFailureMetricsTests()
    {
        _factory = new ObservabilityApiFactory(
            new CapturingLogSink(),
            new Dictionary<string, string?> { ["Authorization:AccountProvisioningClients:0"] = "billing-core" },
            services =>
            {
                services.AddSingleton<ILogger<AuthenticationEvents>>(_rejections);
                services.AddSingleton<ILogger<LedgerAuthorizationResultHandler>>(_denials);
            });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _failures.Dispose();
        _environment.Dispose();
    }

    [Fact]
    public async Task ARequestWithoutToken_CountsMissingTokenOnce()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(ReadRoute, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ReasonsCounted().ShouldBe(["missing_token"]);

        var rejected = _rejections.Events.ShouldHaveSingleItem();

        rejected.Id.ShouldBe(6001);
        rejected.Name.ShouldBe("AuthenticationRejected");
        rejected.Level.ShouldBe(LogLevel.Information);
        rejected.Properties["Reason"].ShouldBe("missing_token");
        rejected.Properties["RequestRoute"].ShouldBe(TemplateOfTheReadRoute);
    }

    [Fact]
    public async Task AMalformedToken_CountsInvalidTokenOnce()
    {
        using var client = _factory.Authenticated("not-a-jwt");

        using var response = await client.GetAsync(ReadRoute, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ReasonsCounted().ShouldBe(["invalid_token"]);
        _rejections.Events.ShouldHaveSingleItem().Properties["Reason"].ShouldBe("invalid_token");
    }

    [Fact]
    public async Task ATokenWithTheWrongAudience_CountsInvalidToken()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(audience: "another-api"));

        using var response = await client.GetAsync(ReadRoute, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ReasonsCounted().ShouldBe(["invalid_token"]);
    }

    [Fact]
    public async Task AnExpiredToken_CountsExpiredOnce()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(lifetime: TimeSpan.FromHours(-1)));

        using var response = await client.GetAsync(ReadRoute, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ReasonsCounted().ShouldBe(["expired"]);
        _rejections.Events.ShouldHaveSingleItem().Properties["Reason"].ShouldBe("expired");
    }

    [Fact]
    public async Task ATokenNotValidYetBeyondTheTolerance_CountsExpired()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(notBeforeOffset: TimeSpan.FromMinutes(5)));

        using var response = await client.GetAsync(ReadRoute, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ReasonsCounted().ShouldBe(["expired"]);
    }

    [Fact]
    public async Task ATokenWithoutTheScopeOfTheRoute_CountsInsufficientScopeOnce()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(scope: "ledger.read", clientId: "reporting"));

        using var response = await Post(client, WriteRoute);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ReasonsCounted().ShouldBe(["insufficient_scope"]);

        var denied = _denials.Events.ShouldHaveSingleItem();

        denied.Id.ShouldBe(6002);
        denied.Name.ShouldBe("AuthorizationDenied");
        denied.Level.ShouldBe(LogLevel.Information);
        denied.Properties["Reason"].ShouldBe("insufficient_scope");
        denied.Properties["ClientId"].ShouldBe("reporting");
        denied.Properties["RequiredScope"].ShouldBe("ledger.write");
        denied.Properties["RequestRoute"].ShouldBe(TemplateOfTheWriteRoute);
        _rejections.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task ATokenWithoutClientId_CountsMissingClientIdAndNotInsufficientScope()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(clientId: null));

        using var response = await client.GetAsync(ReadRoute, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ReasonsCounted().ShouldBe(["missing_client_id"]);
        _denials.Events.ShouldHaveSingleItem().Properties["Reason"].ShouldBe("missing_client_id");
    }

    [Fact]
    public async Task ATokenWithAClientIdLongerThanTheColumn_CountsInvalidClientIdAndKeepsTheValueOutOfTheLog()
    {
        var oversized = new string('c', 5000);
        using var client = _factory.Authenticated(TestTokenFactory.Create(clientId: oversized));

        using var response = await client.GetAsync(ReadRoute, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ReasonsCounted().ShouldBe(["invalid_client_id"]);

        var denied = _denials.Events.ShouldHaveSingleItem();

        denied.Properties["Reason"].ShouldBe("invalid_client_id");
        denied.Properties["ClientId"].ShouldBeNull();
    }

    [Fact]
    public async Task AClientOutsideTheProvisioningList_CountsInsufficientScope()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(scope: "ledger.write", clientId: "pix-core"));

        using var response = await Post(client, "/v1/accounts");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ReasonsCounted().ShouldBe(["insufficient_scope"]);
        _denials.Events.ShouldHaveSingleItem().Properties["ClientId"].ShouldBe("pix-core");
    }

    [Fact]
    public async Task ABadTokenOnAHealthRoute_IsNotCounted()
    {
        using var client = _factory.Authenticated("not-a-jwt");

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ReasonsCounted().ShouldBeEmpty();
        _rejections.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task AValidRequest_CountsNothing()
    {
        using var client = _factory.Authenticated();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ReasonsCounted().ShouldBeEmpty();
    }

    [Fact]
    public async Task NoLabelOfTheFailureCounter_IsAClientId()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(scope: "ledger.read", clientId: "reporting"));

        using var denied = await Post(client, WriteRoute);

        _failures.Measurements.ShouldAllBe(measurement => measurement.Tags.Keys.All(key => key == "reason"));
    }

    [Fact]
    public async Task NothingOfTheTokenReachesTheAuthenticationAndAuthorizationLogs()
    {
        var token = TestTokenFactory.Create(scope: "ledger.read", clientId: "reporting");
        using var client = _factory.Authenticated(token);
        using var rejectedClient = _factory.Authenticated("not-a-jwt");

        using var denied = await Post(client, WriteRoute);
        using var rejected = await rejectedClient.GetAsync(ReadRoute, CancellationToken.None);

        var logged = string.Join(
            '\n',
            _rejections.Events.Concat(_denials.Events)
                .SelectMany(entry => entry.Properties.Values.Select(value => value?.ToString()).Append(entry.Message)));

        logged.ShouldNotBeNullOrWhiteSpace();
        logged.ShouldNotContain(token);
        logged.ShouldNotContain(token[..40]);
        logged.ShouldNotContain("Bearer ey");
        logged.ShouldNotContain("not-a-jwt");
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string route)
    {
        using var body = new StringContent("{}");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return await client.PostAsync(route, body, CancellationToken.None);
    }

    private List<string> ReasonsCounted()
    {
        return [.. _failures.Measurements.Select(measurement => (string)measurement.Tags["reason"]!)];
    }
}
