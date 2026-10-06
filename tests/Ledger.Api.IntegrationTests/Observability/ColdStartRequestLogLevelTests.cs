using Ledger.Api.Observability;
using Microsoft.AspNetCore.Http;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Observability;

[Trait("Category", "Unit")]
public sealed class ColdStartRequestLogLevelTests
{
    private const string AccountsRoute = "HTTP: POST /v1/accounts";
    private const string BalanceRoute = "HTTP: GET /v1/accounts/{accountId}/balance";
    private const string OpenApiRoute = "HTTP: GET /openapi/{documentName}.json";

    private static LogEventLevel Level(
        ColdStartRequestLogLevel selector,
        string route,
        int status,
        double elapsedMilliseconds)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/any";
        context.SetEndpoint(new Endpoint(null, EndpointMetadataCollection.Empty, route));
        context.Response.StatusCode = status;

        return selector.For(context, elapsedMilliseconds, null);
    }

    private static LogEventLevel Level(ColdStartRequestLogLevel selector, string path, int status)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;

        return selector.For(context, 250, null);
    }

    [Fact]
    public void For_TheFirstSlowResponseOfARoute_IsInformationAndTheNextSlowOneIsWarning()
    {
        var selector = new ColdStartRequestLogLevel();

        Level(selector, AccountsRoute, 201, 250).ShouldBe(LogEventLevel.Information);
        Level(selector, AccountsRoute, 201, 250).ShouldBe(LogEventLevel.Warning);
    }

    [Fact]
    public void For_EachRouteHasItsOwnColdStart()
    {
        var selector = new ColdStartRequestLogLevel();

        Level(selector, AccountsRoute, 201, 12).ShouldBe(LogEventLevel.Debug);
        Level(selector, OpenApiRoute, 200, 250).ShouldBe(LogEventLevel.Information);
        Level(selector, BalanceRoute, 200, 250).ShouldBe(LogEventLevel.Information);
        Level(selector, OpenApiRoute, 200, 250).ShouldBe(LogEventLevel.Warning);
        Level(selector, BalanceRoute, 200, 250).ShouldBe(LogEventLevel.Warning);
    }

    [Fact]
    public void For_TheFirstFastResponseOfARoute_IsDebugAndDoesNotExemptTheNextSlowOne()
    {
        var selector = new ColdStartRequestLogLevel();

        Level(selector, AccountsRoute, 201, 12).ShouldBe(LogEventLevel.Debug);
        Level(selector, AccountsRoute, 201, 250).ShouldBe(LogEventLevel.Warning);
    }

    [Fact]
    public void For_HealthChecksBeforeTheFirstRealRequest_DoNotUseUpTheExemption()
    {
        var selector = new ColdStartRequestLogLevel();

        Level(selector, "/health/ready", 200).ShouldBe(LogEventLevel.Verbose);
        Level(selector, AccountsRoute, 201, 250).ShouldBe(LogEventLevel.Information);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(422)]
    public void For_ARefusedRequestBeforeTheFirstSuccess_DoesNotUseUpTheExemption(int status)
    {
        var selector = new ColdStartRequestLogLevel();

        Level(selector, AccountsRoute, status, 250).ShouldBe(LogEventLevel.Information);
        Level(selector, AccountsRoute, 201, 250).ShouldBe(LogEventLevel.Information);
        Level(selector, AccountsRoute, 201, 250).ShouldBe(LogEventLevel.Warning);
    }

    [Fact]
    public void For_TheFirstRequestThatFailsWithAServerError_IsStillErrorAndKeepsTheExemption()
    {
        var selector = new ColdStartRequestLogLevel();

        Level(selector, AccountsRoute, 503, 250).ShouldBe(LogEventLevel.Error);
        Level(selector, AccountsRoute, 201, 250).ShouldBe(LogEventLevel.Information);
    }

    [Fact]
    public void For_AResponseWithoutAMatchedRoute_SharesOneColdStartWhateverThePath()
    {
        var selector = new ColdStartRequestLogLevel();

        Level(selector, "/one", 200).ShouldBe(LogEventLevel.Information);
        Level(selector, "/two", 200).ShouldBe(LogEventLevel.Warning);
    }

    [Fact]
    public void For_EachSelector_KeepsItsOwnColdStart()
    {
        Level(new ColdStartRequestLogLevel(), AccountsRoute, 201, 250).ShouldBe(LogEventLevel.Information);
        Level(new ColdStartRequestLogLevel(), AccountsRoute, 201, 250).ShouldBe(LogEventLevel.Information);
    }
}
