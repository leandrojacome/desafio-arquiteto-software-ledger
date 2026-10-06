using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Tests.Support;

internal class CapturingLogger : ILogger
{
    private readonly List<CapturedLog> _entries = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<CapturedLog> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

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
        var properties = new Dictionary<string, string?>();

        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
            {
                properties[pair.Key] = Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
            }
        }

        var captured = new CapturedLog(logLevel, eventId, formatter(state, exception), properties);

        lock (_gate)
        {
            _entries.Add(captured);
        }
    }

    public CapturedLog Single(int eventId) => Entries.Single(entry => entry.EventId.Id == eventId);

    public bool Contains(int eventId) => Entries.Any(entry => entry.EventId.Id == eventId);
}
