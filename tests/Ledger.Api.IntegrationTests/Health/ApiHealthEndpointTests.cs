using System.Net;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Security;
using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Health;

[Trait("Category", "Integration")]
public sealed class ApiHealthEndpointTests(DefaultTestApiFactory factory) : IClassFixture<DefaultTestApiFactory>
{
    [Fact]
    public async Task Live_AnswersHealthyWithoutAnyDependencyAndForbidsCaching()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        (await BodyOf(response)).ShouldBe("{\"status\":\"Healthy\"}");
    }

    [Fact]
    public async Task Ready_WithoutTheDatabase_IsUnhealthyWithRetryAfterFiveAndNothingButTheState()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(5));
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        (await BodyOf(response)).ShouldBe("{\"status\":\"Unhealthy\"}");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task TheHealthRoutes_AnswerHeadAsWellAsGet(string route)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Head, route);

        using var response = await client.SendAsync(request, CancellationToken.None);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("/health/live", "POST")]
    [InlineData("/health/live", "PUT")]
    [InlineData("/health/live", "DELETE")]
    [InlineData("/health/ready", "POST")]
    [InlineData("/health/ready", "PATCH")]
    public async Task TheHealthRoutes_RefuseAnyMethodButGetAndHead(string route, string method)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route);

        using var response = await client.SendAsync(request, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.OrderBy(verb => verb, StringComparer.Ordinal)
            .ShouldBe(["GET", "HEAD"]);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task TheHealthRoutes_NeedNoTokenAndIgnoreABadOne(string route)
    {
        using var anonymous = factory.CreateClient();
        using var withBadToken = factory.ClientWith("not-a-jwt");

        using var first = await anonymous.GetAsync(route, CancellationToken.None);
        using var second = await withBadToken.GetAsync(route, CancellationToken.None);

        first.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        second.StatusCode.ShouldBe(first.StatusCode);
    }

    [Fact]
    public void TheReadinessOfTheApi_AsksOnlyForThePostgresTheSchemaTheKeysAndTheShutdown()
    {
        var registrations = factory.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations
            .Where(registration => registration.Tags.Contains(HealthCheckTags.Ready))
            .ToList();

        registrations.Select(registration => registration.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["keys", "postgres", "schema", "shutdown"]);
    }

    [Fact]
    public void TheKeysCheck_DegradesTheReadinessAndNeverMakesItUnhealthy()
    {
        var keys = factory.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(registration => registration.Name == "keys");

        keys.FailureStatus.ShouldBe(HealthStatus.Degraded);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("schema")]
    public void TheProbesThatTouchTheDatabase_AreCachedForAFewSeconds(string name)
    {
        var registration = factory.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(candidate => candidate.Name == name);

        registration.Factory(factory.Services).GetType().Name.ShouldBe(nameof(CachedHealthCheck));
        factory.Services.GetRequiredService<IOptions<Ledger.Infrastructure.Resilience.ResilienceOptions>>()
            .Value.Health.CacheSeconds.ShouldBeInRange(1, 10);
    }

    [Fact]
    public void TheApi_RegistersNoCheckOfTheWorker()
    {
        var options = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        options.Registrations.Select(registration => registration.Name)
            .Where(name => name is "rabbitmq" or "broker-circuit" or "outbox-lag" or "heartbeats")
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task TheShutdownCheck_TurnsUnhealthyAsSoonAsTheHostIsAskedToStop()
    {
        using var stopping = new CancellationTokenSource();
        var check = new Ledger.Api.Health.ShutdownHealthCheck(new StubLifetime(stopping.Token));

        var before = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        await stopping.CancelAsync();

        var after = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        before.Status.ShouldBe(HealthStatus.Healthy);
        after.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    private static async Task<string> BodyOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);

        return document.RootElement.GetRawText();
    }

    private sealed class StubLifetime(CancellationToken stopping) : Microsoft.Extensions.Hosting.IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping { get; } = stopping;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
