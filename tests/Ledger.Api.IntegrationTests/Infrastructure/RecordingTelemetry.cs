using System.Collections.Concurrent;
using Ledger.Application.Abstractions;
using Ledger.Application.Integrity;
using Ledger.Application.Outbox;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class RecordingIntegrityTelemetry : IIntegrityTelemetry
{
    private readonly ConcurrentQueue<RecordedRun> _runs = new();

    public IReadOnlyList<RecordedRun> Runs => [.. _runs];

    public IIntegrityRunTelemetry BeginRun(IntegrityMode mode)
    {
        var run = new RecordedRun(mode);

        _runs.Enqueue(run);

        return run;
    }

    internal sealed class RecordedRun(IntegrityMode mode) : IIntegrityRunTelemetry
    {
        private readonly ConcurrentQueue<IntegrityCheck> _violations = new();
        private long _accounts;

        public IntegrityMode Mode { get; } = mode;

        public IReadOnlyList<IntegrityCheck> Violations => [.. _violations];

        public long Accounts => Interlocked.Read(ref _accounts);

        public bool? Clean { get; private set; }

        public bool WasFailed { get; private set; }

        public void Violation(IntegrityCheck check) => _violations.Enqueue(check);

        public void AccountsChecked(long count) => Interlocked.Exchange(ref _accounts, count);

        public void Completed(bool clean) => Clean = clean;

        public void Failed() => WasFailed = true;

        public void Dispose()
        {
        }
    }
}

internal sealed class RecordingOutboxTelemetry : IOutboxTelemetry
{
    private readonly ConcurrentQueue<BrokerCircuitState> _circuitStates = new();
    private readonly ConcurrentQueue<bool> _connections = new();
    private readonly ConcurrentQueue<PublishFailureReason> _failures = new();

    public IReadOnlyList<BrokerCircuitState> CircuitStates => [.. _circuitStates];

    public IReadOnlyList<bool> Connections => [.. _connections];

    public IReadOnlyList<PublishFailureReason> Failures => [.. _failures];

    public IOutboxPollOperation BeginPoll() => new NoOperation();

    public IOutboxPublishOperation BeginPublish(OutboxEnvelope envelope) => new NoOperation();

    public void Published(int count)
    {
    }

    public void PublishFailed(PublishFailureReason reason) => _failures.Enqueue(reason);

    public void Pruned(int count)
    {
    }

    public void Measured(OutboxStats stats)
    {
    }

    public void CircuitStateChanged(BrokerCircuitState state) => _circuitStates.Enqueue(state);

    public void BrokerConnected(bool connected) => _connections.Enqueue(connected);

    private sealed class NoOperation : IOutboxPollOperation, IOutboxPublishOperation
    {
        public void BatchSize(int size)
        {
        }

        public void Confirmed()
        {
        }

        public void Failed(PublishFailureReason reason)
        {
        }

        public void Dispose()
        {
        }
    }
}

internal sealed class RecordingWorkerLoopTelemetry : IWorkerLoopTelemetry
{
    private readonly ConcurrentQueue<WorkerLoop> _succeeded = new();
    private readonly ConcurrentQueue<WorkerLoop> _failed = new();

    public IReadOnlyCollection<WorkerLoop> Succeeded => _succeeded;

    public IReadOnlyCollection<WorkerLoop> Failed => _failed;

    public void CycleSucceeded(WorkerLoop loop) => _succeeded.Enqueue(loop);

    public void CycleFailed(WorkerLoop loop) => _failed.Enqueue(loop);
}
