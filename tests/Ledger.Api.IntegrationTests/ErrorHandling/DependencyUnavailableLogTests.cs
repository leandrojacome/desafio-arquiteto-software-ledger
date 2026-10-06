using System.Net;
using Ledger.Api.ErrorHandling;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Resilience;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ledger.Api.IntegrationTests.ErrorHandling;

[Trait("Category", "Unit")]
public sealed class DependencyUnavailableLogTests
{
    private readonly LogCapture<GlobalExceptionHandler> _logs = new();
    private readonly RecordingProblemDetailsService _problems = new();

    private GlobalExceptionHandler Handler() => new(
        _problems,
        new PostgresTransientFailureClassifier(),
        new PostgresDependencyFailureInspector(),
        Options.Create(new ResilienceOptions()),
        _logs);

    [Theory]
    [InlineData(PostgresErrorCodes.ReadOnlySqlTransaction)]
    [InlineData(PostgresErrorCodes.LockNotAvailable)]
    [InlineData(PostgresErrorCodes.DiskFull)]
    [InlineData(PostgresErrorCodes.CrashShutdown)]
    public async Task ATransientDatabaseFailure_Returns503AndLogsTheSqlStateOnly(string sqlState)
    {
        var context = new DefaultHttpContext();
        var failure = new PostgresException("secret statement text with host db.internal", "ERROR", "ERROR", sqlState);

        var handled = await Handler().TryHandleAsync(context, failure, CancellationToken.None);

        var logged = _logs.Events.ShouldHaveSingleItem();

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe((int)HttpStatusCode.ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().ShouldBe("1");
        logged.Id.ShouldBe(9002);
        logged.Name.ShouldBe("DependencyUnavailable");
        logged.Level.ShouldBe(LogLevel.Warning);
        logged.Properties["SqlState"].ShouldBe(sqlState);
        logged.Message.ShouldContain(sqlState);
        logged.Message.ShouldNotContain("secret");
        logged.Message.ShouldNotContain("db.internal");
    }

    [Fact]
    public async Task ATransientFailureWithoutASqlState_StillReturns503AndLogsANullSqlState()
    {
        var context = new DefaultHttpContext();

        var handled = await Handler().TryHandleAsync(context, new TimeoutException("slow"), CancellationToken.None);

        var logged = _logs.Events.ShouldHaveSingleItem();

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe((int)HttpStatusCode.ServiceUnavailable);
        logged.Id.ShouldBe(9002);
        logged.Properties["SqlState"].ShouldBeNull();
    }

    [Fact]
    public async Task AnUnexpectedFailure_Returns500AndDoesNotEmitTheDependencyEvent()
    {
        var context = new DefaultHttpContext();

        await Handler().TryHandleAsync(context, new InvalidOperationException("bug"), CancellationToken.None);

        context.Response.StatusCode.ShouldBe((int)HttpStatusCode.InternalServerError);
        _logs.Events.ShouldHaveSingleItem().Id.ShouldBe(9001);
    }

    [Fact]
    public async Task TheProblemBody_NeverCarriesTheSqlState()
    {
        var context = new DefaultHttpContext();
        var failure = new PostgresException("text", "ERROR", "ERROR", PostgresErrorCodes.DiskFull);

        await Handler().TryHandleAsync(context, failure, CancellationToken.None);

        var problem = _problems.Written.ShouldHaveSingleItem();

        (problem.ProblemDetails.Detail ?? string.Empty).ShouldNotContain(PostgresErrorCodes.DiskFull);
        problem.ProblemDetails.Extensions.Values
            .Select(value => value?.ToString() ?? string.Empty)
            .ShouldNotContain(PostgresErrorCodes.DiskFull);
    }
}
