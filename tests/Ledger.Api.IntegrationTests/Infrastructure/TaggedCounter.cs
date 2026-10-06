using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class TaggedCounter : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<TaggedMeasurement> _measurements = new();

    public TaggedCounter(string meterName, string instrumentName)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var captured = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                captured[tag.Key] = tag.Value;
            }

            _measurements.Enqueue(new TaggedMeasurement(value, captured));
        });

        _listener.Start();
    }

    public IReadOnlyList<TaggedMeasurement> Measurements => [.. _measurements];

    public long Total => _measurements.Sum(measurement => measurement.Value);

    public long TotalWhere(string tag, string value)
    {
        return _measurements
            .Where(measurement => measurement.Tags.TryGetValue(tag, out var found) && found as string == value)
            .Sum(measurement => measurement.Value);
    }

    public void Dispose() => _listener.Dispose();
}

internal sealed record TaggedMeasurement(long Value, IReadOnlyDictionary<string, object?> Tags);
