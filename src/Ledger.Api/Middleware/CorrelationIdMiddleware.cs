using System.Diagnostics;
using System.Text.RegularExpressions;
using Ledger.Application;
using Ledger.Application.Abstractions;
using Serilog.Context;

namespace Ledger.Api.Middleware;

internal sealed partial class CorrelationIdMiddleware(
    RequestDelegate next,
    IIdGenerator idGenerator,
    ILogger<CorrelationIdMiddleware> logger)
{
    private const string HeaderName = ApiConstants.CorrelationIdHeader;

    private const string ItemKey = "Ledger.CorrelationId";
    private const string LogPropertyName = "CorrelationId";

    public static string Resolve(HttpContext context)
    {
        return context.Items.TryGetValue(ItemKey, out var value) && value is string correlationId
            ? correlationId
            : context.TraceIdentifier;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ReadOrGenerate(context);

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(WriteHeader, (context.Response, correlationId));
        Activity.Current?.SetTag(ActivityTagNames.CorrelationId, correlationId);

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await next(context);
        }
    }

    private static Task WriteHeader(object state)
    {
        var (response, correlationId) = ((HttpResponse, string))state;
        response.Headers[HeaderName] = correlationId;

        return Task.CompletedTask;
    }

    private string ReadOrGenerate(HttpContext context)
    {
        var received = context.Request.Headers[HeaderName];

        if (received.Count == 1 && received[0] is { } candidate && ValidCorrelationId().IsMatch(candidate))
        {
            return candidate;
        }

        if (received.Count > 0)
        {
            LogRejected(logger);
        }

        return idGenerator.NewId().ToString("N");
    }

    [LoggerMessage(
        EventId = 9004,
        EventName = "CorrelationIdRejected",
        Level = LogLevel.Warning,
        Message = "A received X-Correlation-Id was discarded because it does not match the accepted format")]
    private static partial void LogRejected(ILogger logger);

    [GeneratedRegex(@"^[A-Za-z0-9._:-]{8,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ValidCorrelationId();
}
