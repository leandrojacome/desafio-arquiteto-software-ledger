using System.Collections.Concurrent;
using Ledger.Application.Integrity;

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
