using System.Collections.Concurrent;
using System.Diagnostics;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Reads;

internal sealed class ReadSpanCapture : IDisposable
{
    private const string SourceName = "Ledger";

    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<CapturedSpan> _spans = new();

    public ReadSpanCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _spans.Enqueue(new CapturedSpan(
                activity.OperationName,
                activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal)))
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<CapturedSpan> Spans => [.. _spans];

    public void Dispose() => _listener.Dispose();

    internal sealed record CapturedSpan(string Name, IReadOnlyDictionary<string, object?> Tags);
}

internal static class LogEventReading
{
    public static string Text(LogEvent logEvent, string name)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        return logEvent.Properties.TryGetValue(name, out var value)
            ? value is ScalarValue scalar ? scalar.Value?.ToString() ?? string.Empty : value.ToString()
            : string.Empty;
    }

    public static bool Has(LogEvent logEvent, string name)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        return logEvent.Properties.ContainsKey(name);
    }

    public static int EventId(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (logEvent.Properties.TryGetValue("EventId", out var value) && value is StructureValue structure)
        {
            var id = structure.Properties.FirstOrDefault(property => property.Name == "Id")?.Value;

            return id is ScalarValue { Value: int number } ? number : 0;
        }

        return 0;
    }

    public static bool IsAudit(LogEvent logEvent) => Text(logEvent, "SourceContext") == "Ledger.Audit";
}
