using System.Net;
using Ledger.Api.ErrorHandling;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Resilience;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.ErrorHandling;

[Trait("Category", "Unit")]
public sealed class RequestCancellationStatusTests
{
    private readonly LogCapture<GlobalExceptionHandler> _logs = new();
    private readonly RecordingProblemDetailsService _problems = new();

    private GlobalExceptionHandler Handler(int retryAfterSeconds = 1) => new(
        _problems,
        new PostgresTransientFailureClassifier(),
        new PostgresDependencyFailureInspector(),
        Options.Create(new ResilienceOptions { ServiceUnavailableRetryAfterSeconds = retryAfterSeconds }),
        _logs);

    [Fact]
    public async Task AClientThatWentAway_Gets499AndNoBody()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        await cancellation.CancelAsync();

        var handled = await Handler().TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(499);
        _problems.Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task ARequestThatTimedOut_Gets503EvenThoughTheAbortTokenWasCancelledWithIt()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Features.Set<IHttpRequestTimeoutFeature>(new TimedOutFeature(cancellation.Token));
        await cancellation.CancelAsync();

        var handled = await Handler(retryAfterSeconds: 4)
            .TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe((int)HttpStatusCode.ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().ShouldBe("4");
        _problems.Written.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ATimeoutThatDidNotFire_DoesNotHideAClientThatWentAway()
    {
        using var abort = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = abort.Token };
        context.Features.Set<IHttpRequestTimeoutFeature>(new TimedOutFeature(timeout.Token));
        await abort.CancelAsync();

        await Handler().TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        context.Response.StatusCode.ShouldBe(499);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(60)]
    public async Task TheRetryAfterOfA503_FollowsTheConfiguredSeconds(int seconds)
    {
        var context = new DefaultHttpContext();

        await Handler(seconds).TryHandleAsync(context, new TimeoutException("slow"), CancellationToken.None);

        context.Response.StatusCode.ShouldBe((int)HttpStatusCode.ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().ShouldBe(seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class TimedOutFeature(CancellationToken token) : IHttpRequestTimeoutFeature
    {
        public CancellationToken RequestTimeoutToken { get; } = token;

        public void DisableTimeout()
        {
        }
    }
}
