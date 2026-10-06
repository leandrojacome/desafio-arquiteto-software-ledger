using System.Globalization;
using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Resilience;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Ledger.Api.ErrorHandling;

internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ITransientFailureClassifier transientFailureClassifier,
    IDependencyFailureInspector dependencyFailureInspector,
    IOptions<ResilienceOptions> resilience,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private const int ClientClosedRequestStatus = 499;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        var timedOut = RequestTimeoutMarkerMiddleware.TimedOut(httpContext);

        if (!timedOut && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = ClientClosedRequestStatus;

            return true;
        }

        var status = timedOut ? StatusCodes.Status503ServiceUnavailable : StatusFor(exception);
        var code = ProblemCatalog.CodeFor(status);

        Log(exception, status);

        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter =
                resilience.Value.ServiceUnavailableRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = ProblemFactory.Create(httpContext, status, code)
        });
    }

    private int StatusFor(Exception exception)
    {
        return exception switch
        {
            BadHttpRequestException badRequest => badRequest.StatusCode,
            OperationCanceledException => StatusCodes.Status503ServiceUnavailable,
            _ when transientFailureClassifier.IsTransient(exception) => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    private void Log(Exception exception, int status)
    {
        switch (status)
        {
            case >= StatusCodes.Status500InternalServerError when status != StatusCodes.Status503ServiceUnavailable:
                LogUnhandledException(logger, exception);
                break;
            case StatusCodes.Status503ServiceUnavailable:
                LogDependencyUnavailable(logger, exception, dependencyFailureInspector.SqlState(exception));
                break;
            default:
                LogRejection(exception, status);
                break;
        }
    }

    private void LogRejection(Exception exception, int status)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            var exceptionType = exception.GetType().Name;
            LogRejectedRequest(logger, status, exceptionType);
        }
    }

    [LoggerMessage(EventId = 9001, Level = LogLevel.Error,
        Message = "Unhandled exception while processing the request")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9002, EventName = "DependencyUnavailable", Level = LogLevel.Warning,
        Message = "A dependency was unavailable while processing the request (SqlState {SqlState})")]
    private static partial void LogDependencyUnavailable(ILogger logger, Exception exception, string? sqlState);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Information,
        Message = "The request was rejected by the server with status {StatusCode} ({ExceptionType})")]
    private static partial void LogRejectedRequest(ILogger logger, int statusCode, string exceptionType);
}
