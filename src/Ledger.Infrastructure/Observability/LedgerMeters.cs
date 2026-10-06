using System.Diagnostics.Metrics;
using Ledger.Application.Abstractions;
using Ledger.Application.Integrity;
using Ledger.Application.Outbox;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Observability;

internal sealed class LedgerMeters
{
    private const string SecondsUnit = "s";

    private readonly OutboxStatsHolder _outbox;
    private readonly IntegritySuccessTracker _integrity;
    private readonly IWorkerHeartbeat _heartbeat;
    private readonly WorkerLoopSuccesses _loopSuccesses;
    private readonly KeyUsageHolder _keyUsage;

    public LedgerMeters(
        TelemetrySources sources,
        OutboxStatsHolder outbox,
        IntegritySuccessTracker integrity,
        IWorkerHeartbeat heartbeat,
        WorkerLoopSuccesses loopSuccesses,
        KeyUsageHolder keyUsage)
    {
        _outbox = outbox;
        _integrity = integrity;
        _heartbeat = heartbeat;
        _loopSuccesses = loopSuccesses;
        _keyUsage = keyUsage;

        var meter = sources.Meter;

        EntriesRecorded = meter.CreateCounter<long>(
            MetricNames.EntriesRecorded, "{entry}", "Entries confirmed in the database");
        EntriesRejected = meter.CreateCounter<long>(
            MetricNames.EntriesRejected, "{entry}", "Entry requests refused by the business rules");
        EntryDuration = meter.CreateHistogram<double>(
            MetricNames.EntryDuration, SecondsUnit, "Duration of the entry use case");
        IdempotencyReplays = meter.CreateCounter<long>(
            MetricNames.IdempotencyReplays, "{replay}", "Requests answered from the stored result");
        IdempotencyConflicts = meter.CreateCounter<long>(
            MetricNames.IdempotencyConflicts, "{conflict}", "Idempotency keys reused with a different request");
        RecordedAtCorrections = meter.CreateCounter<long>(
            MetricNames.RecordedAtCorrections, "{correction}", "Entries whose recorded instant was moved ahead");
        DbRetries = meter.CreateCounter<long>(
            MetricNames.DbRetries, "{retry}", "Transaction retries before the commit");
        DbCommandDuration = meter.CreateHistogram<double>(
            MetricNames.DbCommandDuration, SecondsUnit, "Duration of each SQL command on the hot path");
        BalanceQueryDuration = meter.CreateHistogram<double>(
            MetricNames.BalanceQueryDuration, SecondsUnit, "Duration of the balance use case");

        AuthFailures = meter.CreateCounter<long>(
            MetricNames.AuthFailures, "{failure}", "Authentication and scope rejections");
        RateLimitRejections = meter.CreateCounter<long>(
            MetricNames.RateLimitRejections, "{rejection}", "Requests rejected by rate or concurrency limits");
        AccountsCreated = meter.CreateCounter<long>(
            MetricNames.AccountsCreated, "{account}", "Accounts created");
        PiiDecrypt = meter.CreateCounter<long>(
            MetricNames.PiiDecrypt, "{account}", "Holder documents read in clear text");
        KeyReloads = meter.CreateCounter<long>(
            MetricNames.KeyReloads, "{reload}", "Key set reloads");
        AuditRecorded = meter.CreateCounter<long>(
            MetricNames.AuditRecorded, "{event}", "Audit events recorded");
        AuditSkipped = meter.CreateCounter<long>(
            MetricNames.AuditSkipped, "{event}", "Audit events skipped");
        RewrapAccounts = meter.CreateCounter<long>(
            MetricNames.RewrapAccounts, "{account}", "Accounts processed by the key rewrap");

        OutboxPublished = meter.CreateCounter<long>(
            MetricNames.OutboxPublished, "{message}", "Outbox messages confirmed by the broker");
        OutboxPublishFailures = meter.CreateCounter<long>(
            MetricNames.OutboxPublishFailures, "{failure}", "Outbox publish attempts that failed");
        OutboxPublishDuration = meter.CreateHistogram<double>(
            MetricNames.OutboxPublishDuration, SecondsUnit, "Time from publish to broker confirmation");
        OutboxPruned = meter.CreateCounter<long>(
            MetricNames.OutboxPruned, "{message}", "Published outbox messages removed");

        WorkerLoopFailures = meter.CreateCounter<long>(
            MetricNames.WorkerLoopFailures, "{failure}", "Worker loop cycles that ended in an exception");

        IntegrityRuns = meter.CreateCounter<long>(
            MetricNames.IntegrityRuns, "{run}", "Integrity check runs");
        IntegrityViolations = meter.CreateCounter<long>(
            MetricNames.IntegrityViolations, "{violation}", "Integrity violations found");
        IntegrityDuration = meter.CreateHistogram<double>(
            MetricNames.IntegrityDuration, SecondsUnit, "Duration of an integrity check run");

        meter.CreateObservableGauge(
            MetricNames.OutboxPending, ObservePending, "{message}", "Outbox messages not yet published");
        meter.CreateObservableGauge(
            MetricNames.OutboxOldestPendingAge, ObserveOldestPendingAge, SecondsUnit, "Age of the oldest pending message");
        meter.CreateObservableGauge(
            MetricNames.OutboxFailed, ObserveFailed, "{message}", "Messages waiting for human intervention");
        meter.CreateObservableGauge(
            MetricNames.BrokerCircuitState, ObserveCircuit, null, "Broker circuit: 0 closed, 1 half open, 2 open");
        meter.CreateObservableGauge(
            MetricNames.BrokerConnected, ObserveConnected, null, "Broker connection: 1 connected, 0 not connected");
        meter.CreateObservableGauge(
            MetricNames.WorkerLastCycle, ObserveWorkerCycles, SecondsUnit, "Instant of the last completed worker loop");
        meter.CreateObservableGauge(
            MetricNames.PiiAccountsBelowActiveKey,
            ObserveAccountsBelowActiveKey,
            "{account}",
            "Accounts whose holder document is sealed with a key version older than the active one");
        meter.CreateObservableGauge(
            MetricNames.WorkerLoopLastSuccess,
            ObserveWorkerLoopSuccesses,
            SecondsUnit,
            "Instant of the last worker loop cycle that finished without an exception");
        meter.CreateObservableGauge(
            MetricNames.IntegrityLastSuccess,
            ObserveIntegritySuccess,
            SecondsUnit,
            "Instant of the last integrity run without findings");
    }

    public Counter<long> EntriesRecorded { get; }

    public Counter<long> EntriesRejected { get; }

    public Histogram<double> EntryDuration { get; }

    public Counter<long> IdempotencyReplays { get; }

    public Counter<long> IdempotencyConflicts { get; }

    public Counter<long> RecordedAtCorrections { get; }

    public Counter<long> DbRetries { get; }

    public Histogram<double> DbCommandDuration { get; }

    public Histogram<double> BalanceQueryDuration { get; }

    public Counter<long> AuthFailures { get; }

    public Counter<long> RateLimitRejections { get; }

    public Counter<long> AccountsCreated { get; }

    public Counter<long> PiiDecrypt { get; }

    public Counter<long> KeyReloads { get; }

    public Counter<long> AuditRecorded { get; }

    public Counter<long> AuditSkipped { get; }

    public Counter<long> RewrapAccounts { get; }

    public Counter<long> OutboxPublished { get; }

    public Counter<long> OutboxPublishFailures { get; }

    public Histogram<double> OutboxPublishDuration { get; }

    public Counter<long> OutboxPruned { get; }

    public Counter<long> WorkerLoopFailures { get; }

    public Counter<long> IntegrityRuns { get; }

    public Counter<long> IntegrityViolations { get; }

    public Histogram<double> IntegrityDuration { get; }

    private static double UnixSeconds(DateTimeOffset instant) => instant.ToUnixTimeMilliseconds() / 1000d;

    private IEnumerable<Measurement<long>> ObservePending()
    {
        if (_outbox.FreshSnapshot is { } stats)
        {
            yield return new Measurement<long>(stats.Pending);
        }
    }

    private IEnumerable<Measurement<double>> ObserveOldestPendingAge()
    {
        if (_outbox.FreshSnapshot is { OldestPendingAgeSeconds: { } age })
        {
            yield return new Measurement<double>(age);
        }
    }

    private IEnumerable<Measurement<long>> ObserveFailed()
    {
        if (_outbox.FreshSnapshot is { } stats)
        {
            yield return new Measurement<long>(stats.Failed);
        }
    }

    private IEnumerable<Measurement<int>> ObserveCircuit()
    {
        if (_outbox.Circuit is { } state)
        {
            yield return new Measurement<int>((int)state);
        }
    }

    private IEnumerable<Measurement<int>> ObserveConnected()
    {
        if (_outbox.Connected is { } connected)
        {
            yield return new Measurement<int>(connected ? 1 : 0);
        }
    }

    private IEnumerable<Measurement<double>> ObserveWorkerCycles()
    {
        if (!_outbox.IsWorker)
        {
            yield break;
        }

        foreach (var loop in Enum.GetValues<WorkerLoop>())
        {
            if (_heartbeat.LastBeat(loop) is { } beat)
            {
                yield return new Measurement<double>(
                    UnixSeconds(beat),
                    new KeyValuePair<string, object?>(TagKeys.Loop, loop.Label()));
            }
        }
    }

    private IEnumerable<Measurement<long>> ObserveAccountsBelowActiveKey()
    {
        if (_keyUsage.AccountsBelowActive is { } accounts)
        {
            yield return new Measurement<long>(accounts);
        }
    }

    private IEnumerable<Measurement<double>> ObserveWorkerLoopSuccesses()
    {
        foreach (var (loop, seconds) in _loopSuccesses.Snapshot())
        {
            yield return new Measurement<double>(
                seconds,
                new KeyValuePair<string, object?>(TagKeys.Loop, loop.Label()));
        }
    }

    private IEnumerable<Measurement<double>> ObserveIntegritySuccess()
    {
        foreach (var (mode, seconds) in _integrity.Snapshot())
        {
            yield return new Measurement<double>(
                seconds,
                new KeyValuePair<string, object?>(TagKeys.Mode, mode.MetricLabel()));
        }
    }
}
