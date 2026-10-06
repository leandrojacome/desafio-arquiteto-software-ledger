using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Ledger.Api.IntegrationTests.Persistence.Support;

internal sealed class MeterCapture : IDisposable
{
    private const string MeterName = "Ledger";

    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<Measurement> _measurements = new();

    public MeterCapture(string instrumentName)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterName && instrument.Name == instrumentName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _measurements.Enqueue(new Measurement(instrument.Name, value, ToDictionary(tags))));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            _measurements.Enqueue(new Measurement(instrument.Name, value, ToDictionary(tags))));

        _listener.Start();
    }

    public IReadOnlyList<Measurement> Measurements => [.. _measurements];

    public void Dispose() => _listener.Dispose();

    private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var values = new Dictionary<string, object?>();

        foreach (var tag in tags)
        {
            values[tag.Key] = tag.Value;
        }

        return values;
    }

    internal sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);
}
