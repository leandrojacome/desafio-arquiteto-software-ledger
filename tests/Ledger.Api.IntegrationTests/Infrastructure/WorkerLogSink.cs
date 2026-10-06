using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Infrastructure;

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
