using System.Diagnostics;
using System.Security.Claims;
using Ledger.Api.Middleware;
using Ledger.Api.Security;
using Ledger.Application;
using Microsoft.AspNetCore.Http;

namespace Ledger.Api.IntegrationTests.Observability;

[Trait("Category", "Unit")]
public sealed class ClientIdActivityMiddlewareTests
{
    private const string Source = "Ledger.Api.IntegrationTests.ClientIdActivity";

    [Fact]
    public void TheTagName_IsTheOneTheMetricsReadAndTheCorrelationTagKeepsItsName()
    {
        ActivityTagNames.ClientId.ShouldBe("ledger.client_id");
        ActivityTagNames.CorrelationId.ShouldBe("ledger.correlation_id");
    }

    [Fact]
    public async Task AnAuthenticatedRequest_TagsTheCurrentActivityWithTheClientId()
    {
        using var listener = Listen();
        using var activitySource = new ActivitySource(Source);
        using var activity = activitySource.StartActivity("request");
        var context = ContextOf("pix-core");
        var reached = false;

        await new ClientIdActivityMiddleware(_ =>
        {
            reached = true;

            return Task.CompletedTask;
        }).InvokeAsync(context);

        reached.ShouldBeTrue();
        activity.ShouldNotBeNull().GetTagItem(ActivityTagNames.ClientId).ShouldBe("pix-core");
    }

    [Fact]
    public async Task ARequestWithoutClientId_LeavesTheActivityUntagged()
    {
        using var listener = Listen();
        using var activitySource = new ActivitySource(Source);
        using var activity = activitySource.StartActivity("request");
        var context = ContextOf(null);

        await new ClientIdActivityMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        activity.ShouldNotBeNull().GetTagItem(ActivityTagNames.ClientId).ShouldBeNull();
    }

    [Fact]
    public async Task WithoutAnActivity_TheRequestStillGoesThrough()
    {
        var context = ContextOf("pix-core");
        var reached = false;

        await new ClientIdActivityMiddleware(_ =>
        {
            reached = true;

            return Task.CompletedTask;
        }).InvokeAsync(context);

        reached.ShouldBeTrue();
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    private static DefaultHttpContext ContextOf(string? clientId)
    {
        var claims = new List<Claim>();

        if (clientId is not null)
        {
            claims.Add(new Claim(ClaimNames.ClientId, clientId));
        }

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"))
        };
    }
}
