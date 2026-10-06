using Ledger.Application.Outbox;
using Ledger.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Observability;

internal sealed class OutboxStatsHolder
{
    private const int NotReported = -1;
    private const int StalePeriods = 3;

    private readonly TimeProvider _timeProvider;
    private OutboxReading? _reading;
    private int _circuit = NotReported;
    private int _connected = NotReported;
    private int _worker;

    public OutboxStatsHolder(TimeProvider timeProvider, IOptions<OutboxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _timeProvider = timeProvider;
        StartedAt = timeProvider.GetUtcNow();
        StaleAfter = TimeSpan.FromSeconds((long)options.Value.MeasureIntervalSeconds * StalePeriods);
    }

    public DateTimeOffset StartedAt { get; }

    public TimeSpan StaleAfter { get; }

    public bool IsWorker => Volatile.Read(ref _worker) == 1;

    public OutboxStats? Snapshot => Volatile.Read(ref _reading)?.Stats;

    public DateTimeOffset? MeasuredAt => Volatile.Read(ref _reading)?.MeasuredAt;

    public OutboxStats? FreshSnapshot
    {
        get
        {
            var reading = Volatile.Read(ref _reading);

            return reading is not null && _timeProvider.GetUtcNow() - reading.MeasuredAt <= StaleAfter
                ? reading.Stats
                : null;
        }
    }

    public BrokerCircuitState? Circuit
    {
        get
        {
            var value = Volatile.Read(ref _circuit);

            return value == NotReported ? null : (BrokerCircuitState)value;
        }
    }

    public bool? Connected
    {
        get
        {
            var value = Volatile.Read(ref _connected);

            return value == NotReported ? null : value == 1;
        }
    }

    public void RecordStats(OutboxStats stats)
    {
        MarkWorker();
        Volatile.Write(ref _reading, new OutboxReading(stats, _timeProvider.GetUtcNow()));
    }

    public void RecordCircuit(BrokerCircuitState state)
    {
        MarkWorker();
        Volatile.Write(ref _circuit, (int)state);
    }

    public void RecordConnected(bool connected)
    {
        MarkWorker();
        Volatile.Write(ref _connected, connected ? 1 : 0);
    }

    public void MarkWorker() => Volatile.Write(ref _worker, 1);

    private sealed record OutboxReading(OutboxStats Stats, DateTimeOffset MeasuredAt);
}
