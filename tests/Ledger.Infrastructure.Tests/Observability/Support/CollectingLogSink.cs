using System.Collections.Concurrent;
using System.Globalization;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Ledger.Infrastructure.Tests.Observability.Support;

internal sealed class CollectingLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events => [.. _events];

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        _events.Enqueue(logEvent);
    }

    public string Json(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        new RenderedCompactJsonFormatter().Format(logEvent, writer);

        return writer.ToString();
    }

    public string AllJson()
    {
        return string.Join('\n', Events.Select(Json));
    }

    public static string Property(LogEvent logEvent, string name)
    {
        return logEvent.Properties.TryGetValue(name, out var value) ? Render(value) : string.Empty;
    }

    private static string Render(LogEventPropertyValue value)
    {
        using var writer = new StringWriter();
        value.Render(writer, null, CultureInfo.InvariantCulture);

        return writer.ToString();
    }
}
