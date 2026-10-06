using System.Diagnostics.Metrics;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class MeterCounter : IDisposable
{
    private readonly MeterListener _listener = new();
    private long _total;

    public MeterCounter(string meterName, string instrumentName)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>(
            (_, measurement, _, _) => Interlocked.Add(ref _total, measurement));
        _listener.Start();
    }

    public long Total => Interlocked.Read(ref _total);

    public void Dispose() => _listener.Dispose();
}
