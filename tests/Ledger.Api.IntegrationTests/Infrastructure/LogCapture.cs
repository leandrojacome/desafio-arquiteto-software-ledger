using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

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
