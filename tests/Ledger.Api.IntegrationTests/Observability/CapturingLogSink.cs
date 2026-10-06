using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Ledger.Api.IntegrationTests.Observability;

internal sealed class CapturingLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events => [.. _events];

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        _events.Enqueue(logEvent);
    }

    public static string Json(LogEvent logEvent)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        new RenderedCompactJsonFormatter().Format(logEvent, writer);

        return writer.ToString().TrimEnd();
    }

    public string Everything()
    {
        var builder = new StringBuilder();

        foreach (var logEvent in Events)
        {
            builder.AppendLine(Json(logEvent));

            if (logEvent.Exception is { } exception)
            {
                builder.AppendLine(exception.ToString());
            }
        }

        return builder.ToString();
    }

    public static string Property(LogEvent logEvent, string name)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (!logEvent.Properties.TryGetValue(name, out var value))
        {
            return string.Empty;
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        value.Render(writer, null, CultureInfo.InvariantCulture);

        return writer.ToString();
    }
}
