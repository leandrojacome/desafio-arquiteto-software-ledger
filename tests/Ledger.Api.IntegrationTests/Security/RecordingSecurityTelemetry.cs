using System.Collections.Concurrent;
using Ledger.Application.Abstractions;

namespace Ledger.Api.IntegrationTests.Security;

internal sealed class RecordingSecurityTelemetry : ISecurityTelemetry
{
    private readonly ConcurrentQueue<(string EventType, string Outcome)> _recorded = new();
    private readonly ConcurrentQueue<string> _skipped = new();

    public IReadOnlyCollection<(string EventType, string Outcome)> Recorded => _recorded;

    public IReadOnlyCollection<string> Skipped => _skipped;

    public void AuditRecorded(string eventType, string outcome) => _recorded.Enqueue((eventType, outcome));

    public void AuditSkipped(string reason) => _skipped.Enqueue(reason);

    public void AuthFailed(string reason)
    {
    }

    public void RateLimitRejected(string policy)
    {
    }

    public void AccountCreated()
    {
    }

    public IAccountCreationOperation BeginCreateAccount() => new NoOperation();

    public void PiiDecrypted(string purpose, int accounts)
    {
    }

    public void KeyReloaded(bool succeeded)
    {
    }

    public IRewrapBatchOperation BeginRewrapBatch() => new NoOperation();

    public void AccountsRewrapped(int rewrapped, int failed)
    {
    }

    public void KeyUsageObserved(long accountsBelowActive, bool anomalous)
    {
    }

    private sealed class NoOperation : IAccountCreationOperation, IRewrapBatchOperation
    {
        public void Outcome(string outcome)
        {
        }

        public void Complete(int rewrapped, int failed)
        {
        }

        public void Dispose()
        {
        }
    }
}
