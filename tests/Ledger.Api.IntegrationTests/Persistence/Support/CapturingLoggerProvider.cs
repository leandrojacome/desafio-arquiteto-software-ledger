using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Ledger.Api.IntegrationTests.Persistence.Support;

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEvent> _events = new();

    public IReadOnlyList<CapturedLogEvent> Events => [.. _events];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_events);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<CapturedLogEvent> events) : ILogger
    {
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
            var properties = new Dictionary<string, object?>();

            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    properties[pair.Key] = pair.Value;
                }
            }

            events.Enqueue(new CapturedLogEvent(eventId.Id, logLevel, formatter(state, exception), properties, exception));
        }
    }
}

internal sealed record CapturedLogEvent(
    int EventId,
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Exception? Exception);
