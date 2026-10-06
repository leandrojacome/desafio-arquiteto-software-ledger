using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Ledger.Api.ErrorHandling;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Security;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Resilience;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ledger.Api.IntegrationTests.ErrorHandling;

[Trait("Category", "Integration")]
public sealed class TimeoutStatusTests
{
    private readonly LogCapture<GlobalExceptionHandler> _logs = new();
    private readonly RecordingProblemDetailsService _problems = new();

    [Fact]
    public async Task ATimedOutRequestWhoseCommandFailedWithANonCancellationError_Gets503NotAn499Nor500()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Features.Set<IHttpRequestTimeoutFeature>(new TimedOutFeature(cancellation.Token));
        await cancellation.CancelAsync();

        var handled = await Handler().TryHandleAsync(
            context,
            new NpgsqlException("connection broken by the cancellation of the command"),
            CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe((int)HttpStatusCode.ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().ShouldBe("3");
        _problems.Written.ShouldHaveSingleItem().ProblemDetails.Extensions["code"].ShouldBe("SERVICE_UNAVAILABLE");
        _logs.Events.ShouldHaveSingleItem().Id.ShouldBe(9002);
    }

    [Fact]
    public async Task ATimedOutRequestWithAnUnexpectedBug_StillAnswers503BecauseTheCallerCannotTellTheOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Features.Set<IHttpRequestTimeoutFeature>(new TimedOutFeature(cancellation.Token));
        await cancellation.CancelAsync();

        await Handler().TryHandleAsync(context, new InvalidOperationException("bug"), CancellationToken.None);

        context.Response.StatusCode.ShouldBe((int)HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task AClientThatLeftBeforeTheTimeout_StillGets499WhateverTheExceptionIs()
    {
        using var abort = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = abort.Token };
        context.Features.Set<IHttpRequestTimeoutFeature>(new TimedOutFeature(timeout.Token));
        await abort.CancelAsync();

        await Handler().TryHandleAsync(context, new NpgsqlException("connection broken"), CancellationToken.None);

        context.Response.StatusCode.ShouldBe(499);
        _problems.Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task AStalledCommand_ThatFailsWithoutACancellationWhenTheTimeoutFires_ComesBackAs503WithRetryAfter()
    {
        using var factory = TestApiFactory.With(new Dictionary<string, string?>
        {
            ["Resilience:RequestTimeoutSeconds"] = "1",
            ["Resilience:ServiceUnavailableRetryAfterSeconds"] = "4"
        });
        using var client = factory.ClientWith(TokenForge.Hmac());
        var watch = Stopwatch.StartNew();

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Timeout, CancellationToken.None);

        watch.Stop();

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(4));
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");
        watch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

        problem.RootElement.GetProperty("code").GetString().ShouldBe("SERVICE_UNAVAILABLE");
        problem.RootElement.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.RootElement.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    private GlobalExceptionHandler Handler() => new(
        _problems,
        new PostgresTransientFailureClassifier(),
        new PostgresDependencyFailureInspector(),
        Options.Create(new ResilienceOptions { ServiceUnavailableRetryAfterSeconds = 3 }),
        _logs);

    private sealed class TimedOutFeature(CancellationToken token) : IHttpRequestTimeoutFeature
    {
        public CancellationToken RequestTimeoutToken { get; } = token;

        public void DisableTimeout()
        {
        }
    }
}
