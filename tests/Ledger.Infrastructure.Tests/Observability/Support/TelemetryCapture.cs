using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ledger.Infrastructure.Tests.Observability.Support;

internal sealed class TelemetryCapture : IDisposable
{
    private readonly MeterListener _meterListener = new();
    private readonly ActivityListener _activityListener;
    private readonly ConcurrentQueue<CapturedMeasurement> _measurements = new();
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ConcurrentDictionary<string, Instrument> _instruments = new();

    public TelemetryCapture(Meter meter, ActivitySource source)
    {
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (!ReferenceEquals(instrument.Meter, meter))
            {
                return;
            }

            _instruments[instrument.Name] = instrument;
            listener.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => Record(instrument, value, tags));
        _meterListener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => Record(instrument, value, tags));
        _meterListener.SetMeasurementEventCallback<int>(
            (instrument, value, tags, _) => Record(instrument, value, tags));
        _meterListener.Start();

        _activityListener = new ActivityListener
        {
            ShouldListenTo = candidate => ReferenceEquals(candidate, source),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _activities.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(_activityListener);
    }

    public IReadOnlyCollection<string> InstrumentNames => [.. _instruments.Keys];

    public IReadOnlyList<CapturedMeasurement> All => [.. _measurements];

    public IReadOnlyList<Activity> Activities => [.. _activities];

    public Instrument Instrument(string name) => _instruments[name];

    public IReadOnlyList<CapturedMeasurement> Of(string instrument) =>
        [.. _measurements.Where(measurement => measurement.Instrument == instrument)];

    public double Sum(string instrument, params (string Key, string Value)[] tags) =>
        Of(instrument).Where(measurement => measurement.Has(tags)).Sum(measurement => measurement.Value);

    public int Count(string instrument, params (string Key, string Value)[] tags) =>
        Of(instrument).Count(measurement => measurement.Has(tags));

    public IReadOnlyList<CapturedMeasurement> Observe(string instrument)
    {
        var before = _measurements.Count;

        _meterListener.RecordObservableInstruments();

        return [.. _measurements.Skip(before).Where(measurement => measurement.Instrument == instrument)];
    }

    public Activity SingleActivity(string name) => _activities.Single(activity => activity.OperationName == name);

    public void Dispose()
    {
        _meterListener.Dispose();
        _activityListener.Dispose();
    }

    private void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct
    {
        var captured = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            captured[tag.Key] = tag.Value;
        }

        _measurements.Enqueue(new CapturedMeasurement(
            instrument.Name,
            instrument.Unit,
            Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
            captured));
    }
}
