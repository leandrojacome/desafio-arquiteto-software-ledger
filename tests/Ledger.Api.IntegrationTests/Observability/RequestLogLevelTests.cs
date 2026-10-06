using Ledger.Api.Observability;
using Microsoft.AspNetCore.Http;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Observability;

[Trait("Category", "Unit")]
public sealed class RequestLogLevelTests
{
    private static LogEventLevel Level(string path, int status, double elapsedMilliseconds, Exception? exception = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;

        return RequestLogLevel.For(context, elapsedMilliseconds, exception);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(599)]
    public void For_ServerError_IsError(int status)
    {
        Level("/v1/accounts/x/entries", status, 5).ShouldBe(LogEventLevel.Error);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(404)]
    public void For_AnyStatusWithAnException_IsError(int status)
    {
        Level("/v1/accounts/x/entries", status, 5, new InvalidOperationException("boom")).ShouldBe(LogEventLevel.Error);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/HEALTH/live")]
    [InlineData("/health")]
    public void For_HealthRoutes_AreVerbose(string path)
    {
        Level(path, 200, 5).ShouldBe(LogEventLevel.Verbose);
        Level(path, 200, 900).ShouldBe(LogEventLevel.Verbose);
    }

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/health/live")]
    public void For_TheServiceUnavailableAnswerOfAHealthRoute_IsVerboseBecauseItIsTheExpectedAnswerOfAnUnreadyInstance(string path)
    {
        Level(path, 503, 3).ShouldBe(LogEventLevel.Verbose);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    public void For_AnyOtherServerErrorOfAHealthRoute_IsStillError(int status)
    {
        Level("/health/ready", status, 3).ShouldBe(LogEventLevel.Error);
    }

    [Fact]
    public void For_AnExceptionOnAHealthRoute_IsStillError()
    {
        Level("/health/ready", 503, 3, new InvalidOperationException("boom")).ShouldBe(LogEventLevel.Error);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    [InlineData(429)]
    public void For_ClientError_IsInformation(int status)
    {
        Level("/v1/accounts/x/entries", status, 5).ShouldBe(LogEventLevel.Information);
        Level("/v1/accounts/x/entries", status, 900).ShouldBe(LogEventLevel.Information);
    }

    [Theory]
    [InlineData(200, 150.01)]
    [InlineData(201, 151)]
    [InlineData(200, 5000)]
    public void For_SuccessSlowerThan150Milliseconds_IsWarning(int status, double elapsed)
    {
        Level("/v1/accounts/x/balance", status, elapsed).ShouldBe(LogEventLevel.Warning);
    }

    [Theory]
    [InlineData(200, 0)]
    [InlineData(200, 12.4)]
    [InlineData(201, 150)]
    [InlineData(204, 149.99)]
    public void For_SuccessWithin150Milliseconds_IsDebug(int status, double elapsed)
    {
        Level("/v1/accounts/x/balance", status, elapsed).ShouldBe(LogEventLevel.Debug);
    }

    [Fact]
    public void For_RouteThatOnlyStartsLikeHealth_IsNotTreatedAsAHealthCheck()
    {
        Level("/healthz", 200, 5).ShouldBe(LogEventLevel.Debug);
        Level("/v1/health", 200, 5).ShouldBe(LogEventLevel.Debug);
    }
}
