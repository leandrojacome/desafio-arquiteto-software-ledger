using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class LogCapture<T> : ILogger<T>
{
    private readonly ConcurrentQueue<CapturedEvent> _events = new();

    public IReadOnlyList<CapturedEvent> Events => [.. _events];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var (key, value) in pairs)
            {
                properties[key] = value;
            }
        }

        _events.Enqueue(new CapturedEvent(logLevel, eventId.Id, eventId.Name, formatter(state, exception), properties));
    }
}

internal sealed record CapturedEvent(
    LogLevel Level,
    int Id,
    string? Name,
    string Message,
    IReadOnlyDictionary<string, object?> Properties);

internal sealed class WorkerLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<SunkEvent> _events = new();

    public IReadOnlyList<SunkEvent> Events => [.. _events];

    public IReadOnlyList<SunkEvent> WithId(int id) => [.. _events.Where(captured => captured.Id == id)];

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var id = 0;

        if (logEvent.Properties.TryGetValue("EventId", out var eventId) && eventId is StructureValue structure)
        {
            var identifier = structure.Properties.FirstOrDefault(property => property.Name == "Id")?.Value;

            id = identifier is ScalarValue { Value: int value } ? value : 0;
        }

        var properties = logEvent.Properties.ToDictionary(
            pair => pair.Key,
            pair => pair.Value is ScalarValue { Value: { } scalar } ? scalar : (object)pair.Value.ToString());

        _events.Enqueue(new SunkEvent(id, logEvent.Level, logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture), properties));
    }
}

internal sealed record SunkEvent(
    int Id,
    LogEventLevel Level,
    string Message,
    IReadOnlyDictionary<string, object> Properties);
