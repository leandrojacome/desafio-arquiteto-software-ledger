using System.Diagnostics;
using System.Diagnostics.Metrics;
using Ledger.Infrastructure.Messaging;
using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Infrastructure.Tests.Observability.Support;

internal sealed class TestTelemetry : IDisposable
{
    public TestTelemetry()
    {
        Meter = new Meter(Telemetry.Name);
        Source = new ActivitySource(Telemetry.Name);
        Sources = new TelemetrySources(Meter, Source);
        Capture = new TelemetryCapture(Meter, Source);
        Outbox = new OutboxStatsHolder(Time, Options.Create(new OutboxOptions()));
        Meters = new LedgerMeters(Sources, Outbox, Integrity, Heartbeat, LoopSuccesses, KeyUsage);
        Entries = new EntryTelemetry(Sources, Meters, Clients, Time);
        Reads = new ReadTelemetry(Sources, Meters, Time);
        Security = new SecurityTelemetry(Sources, Meters, KeyUsage);
        LoopPort = new WorkerLoopTelemetry(Meters, LoopSuccesses, Time);
        OutboxPort = new OutboxTelemetry(Sources, Meters, Outbox, Time);
        IntegrityPort = new IntegrityTelemetry(Sources, Meters, Integrity, Time);
        Db = new DbTelemetry(Meters);
    }

    public Meter Meter { get; }

    public ActivitySource Source { get; }

    public TelemetrySources Sources { get; }

    public TelemetryCapture Capture { get; }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    public OutboxStatsHolder Outbox { get; }

    public WorkerLoopSuccesses LoopSuccesses { get; } = new();

    public KeyUsageHolder KeyUsage { get; } = new();

    public IntegritySuccessTracker Integrity { get; } = new();

    public StubWorkerHeartbeat Heartbeat { get; } = new();

    public ClientLabels Clients { get; } = new();

    public LedgerMeters Meters { get; }

    public EntryTelemetry Entries { get; }

    public ReadTelemetry Reads { get; }

    public SecurityTelemetry Security { get; }

    public WorkerLoopTelemetry LoopPort { get; }

    public OutboxTelemetry OutboxPort { get; }

    public IntegrityTelemetry IntegrityPort { get; }

    public DbTelemetry Db { get; }

    public void Dispose()
    {
        Capture.Dispose();
        Source.Dispose();
        Meter.Dispose();
    }
}
