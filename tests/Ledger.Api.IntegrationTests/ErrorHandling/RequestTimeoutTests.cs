using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.ErrorHandling;

[Trait("Category", "Integration")]
public sealed class RequestTimeoutTests
{
    private const string SlowRoute = "/slow-test-route";

    private static ObservabilityApiFactory FactoryWith(
        Dictionary<string, string?> overrides,
        SlowRouteProbe? probe = null) =>
        new(
            new CapturingLogSink(),
            overrides,
            services =>
            {
                services.AddSingleton(probe ?? new SlowRouteProbe());
                services.AddTransient<IStartupFilter, SlowRouteStartupFilter>();
            });

    [Fact]
    public async Task ARequestThatOutlivesTheTimeout_Returns503WithTheConfiguredRetryAfterAndTheProblemBody()
    {
        var probe = new SlowRouteProbe();
        using var factory = FactoryWith(
            new Dictionary<string, string?>
            {
                ["Resilience:RequestTimeoutSeconds"] = "1",
                ["Resilience:ServiceUnavailableRetryAfterSeconds"] = "7"
            },
            probe);
        using var client = factory.Authenticated();
        var watch = Stopwatch.StartNew();

        using var response = await client.GetAsync(SlowRoute, CancellationToken.None);

        watch.Stop();

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(7));
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");
        watch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
        await probe.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

        problem.RootElement.GetProperty("code").GetString().ShouldBe("SERVICE_UNAVAILABLE");
        problem.RootElement.GetProperty("status").GetInt32().ShouldBe(503);
        problem.RootElement.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ARequestThatFinishesInsideTheTimeout_IsNotTouched()
    {
        using var factory = FactoryWith(new Dictionary<string, string?> { ["Resilience:RequestTimeoutSeconds"] = "10" });
        using var client = factory.Authenticated();

        using var response = await client.GetAsync("/does-not-exist", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public void TheHealthEndpoints_AreExemptFromTheRequestTimeout(string route)
    {
        using var factory = FactoryWith(new Dictionary<string, string?>());

        var endpoints = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(candidate => candidate.RoutePattern.RawText == route)
            .ToList();

        endpoints.ShouldNotBeEmpty();
        endpoints.ShouldAllBe(endpoint => endpoint.Metadata.GetMetadata<DisableRequestTimeoutAttribute>() != null);
    }

    [Fact]
    public void TheTimeoutPolicy_FollowsTheConfiguredSecondsAndAnswers503()
    {
        using var factory = FactoryWith(new Dictionary<string, string?> { ["Resilience:RequestTimeoutSeconds"] = "4" });

        var policy = factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestTimeoutOptions>>()
            .Value.DefaultPolicy.ShouldNotBeNull();

        policy.Timeout.ShouldBe(TimeSpan.FromSeconds(4));
        policy.TimeoutStatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        policy.WriteTimeoutResponse.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("61")]
    public void ARequestTimeoutOutsideTheRange_RefusesToStart(string seconds)
    {
        using var factory = FactoryWith(new Dictionary<string, string?> { ["Resilience:RequestTimeoutSeconds"] = seconds });

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        failure.ToString().ShouldContain("RequestTimeoutSeconds");
    }

    [Fact]
    public async Task TheReadinessRetryAfter_FollowsTheConfiguredValue()
    {
        using var factory = FactoryWith(new Dictionary<string, string?> { ["Resilience:Health:RetryAfterSeconds"] = "9" });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(9));
    }

    private sealed class SlowRouteProbe
    {
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SlowRouteStartupFilter(SlowRouteProbe probe) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return app =>
            {
                next(app);

                app.Use(async (context, following) =>
                {
                    if (context.Request.Path != SlowRoute)
                    {
                        await following(context);

                        return;
                    }

                    try
                    {
                        await Task.Delay(Timeout.Infinite, context.RequestAborted);
                    }
                    catch (OperationCanceledException)
                    {
                        probe.Cancelled.TrySetResult();

                        throw;
                    }
                });
            };
        }
    }
}
