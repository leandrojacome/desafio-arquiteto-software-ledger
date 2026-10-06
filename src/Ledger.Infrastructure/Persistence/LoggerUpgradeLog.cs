using System.Globalization;
using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;

namespace Ledger.Infrastructure.Persistence;

internal sealed partial class LoggerUpgradeLog(ILogger logger) : IUpgradeLog
{
    private int _failureReported;

    public void LogTrace(string format, params object[] args)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            var message = Render(format, args);
            Trace(logger, message);
        }
    }

    public void LogDebug(string format, params object[] args)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var message = Render(format, args);
            Debug(logger, message);
        }
    }

    public void LogInformation(string format, params object[] args)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            var message = Render(format, args);
            Information(logger, message);
        }
    }

    public void LogWarning(string format, params object[] args) => Warning(logger, Render(format, args));

    public void LogError(string format, params object[] args) => ReportFailure(Render(format, args));

    public void LogError(Exception ex, string format, params object[] args) =>
        ReportFailure($"{Render(format, args)}{Environment.NewLine}{ex}");

    private static string Render(string format, object[] args) =>
        string.Format(CultureInfo.InvariantCulture, format, args);

    private static string FirstLine(string message)
    {
        var end = message.AsSpan().IndexOfAny('\r', '\n');

        return end < 0 ? message : message[..end];
    }

    private void ReportFailure(string message)
    {
        if (Interlocked.Exchange(ref _failureReported, 1) == 0)
        {
            var summary = FirstLine(message);

            Error(logger, summary);
        }
        else if (logger.IsEnabled(LogLevel.Debug))
        {
            Debug(logger, message);
        }
    }

    [LoggerMessage(EventId = 5003, Level = LogLevel.Information, Message = "Migration: {Message:l}")]
    private static partial void Information(ILogger logger, string message);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Warning, Message = "Migration: {Message:l}")]
    private static partial void Warning(ILogger logger, string message);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Error, Message = "Migration failed: {Message:l}")]
    private static partial void Error(ILogger logger, string message);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Trace, Message = "Migration: {Message:l}")]
    private static partial void Trace(ILogger logger, string message);

    [LoggerMessage(EventId = 5007, Level = LogLevel.Debug, Message = "Migration: {Message:l}")]
    private static partial void Debug(ILogger logger, string message);
}
