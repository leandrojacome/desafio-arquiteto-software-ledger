using Ledger.Application.Integrity;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class FaultyIntegritySessions(IIntegritySessions inner) : IIntegritySessions
{
    public Func<Task>? BeforeCheckHeads { get; set; }

    public Func<Task>? BeforeCheckChain { get; set; }

    public Task? RecentRunsWait { get; set; }

    public int Opened { get; private set; }

    public async Task<IIntegritySession?> TryBeginRunAsync(IntegrityMode mode, CancellationToken cancellationToken)
    {
        var session = await inner.TryBeginRunAsync(mode, cancellationToken);

        if (session is not null)
        {
            Opened++;
        }

        return session is null ? null : new FaultySession(session, this, mode);
    }

    public async Task<IIntegritySession> OpenAsync(CancellationToken cancellationToken) =>
        new FaultySession(await inner.OpenAsync(cancellationToken), this, null);

    private sealed class FaultySession(IIntegritySession inner, FaultyIntegritySessions owner, IntegrityMode? mode)
        : IIntegritySession
    {
        public Task<DateTimeOffset> GetDatabaseNowAsync(CancellationToken cancellationToken) =>
            inner.GetDatabaseNowAsync(cancellationToken);

        public Task<IntegrityRunRecord?> FindLastRunAsync(IntegrityMode mode, CancellationToken cancellationToken) =>
            inner.FindLastRunAsync(mode, cancellationToken);

        public Task<IReadOnlyList<AccountId>> ReadRecentAccountsAsync(
            DateTimeOffset start,
            DateTimeOffset end,
            CancellationToken cancellationToken) =>
            inner.ReadRecentAccountsAsync(start, end, cancellationToken);

        public Task<IReadOnlyList<AccountId>> ReadAccountBatchAsync(
            AccountId? after,
            int size,
            CancellationToken cancellationToken) =>
            inner.ReadAccountBatchAsync(after, size, cancellationToken);

        public async Task<IReadOnlyList<HeadRow>> CheckHeadsAsync(
            IReadOnlyList<AccountId> accounts,
            CancellationToken cancellationToken)
        {
            if (owner.BeforeCheckHeads is { } hook)
            {
                await hook();
            }

            return await inner.CheckHeadsAsync(accounts, cancellationToken);
        }

        public async Task<IReadOnlyList<ChainRow>> CheckChainAsync(
            DateTimeOffset start,
            DateTimeOffset end,
            CancellationToken cancellationToken)
        {
            if (mode == IntegrityMode.Recent && owner.RecentRunsWait is { } wait)
            {
                await wait.WaitAsync(cancellationToken);
            }

            if (owner.BeforeCheckChain is { } hook)
            {
                await hook();
            }

            return await inner.CheckChainAsync(start, end, cancellationToken);
        }

        public Task<long> CountEntriesAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken) =>
            inner.CountEntriesAsync(start, end, cancellationToken);

        public Task<AccountIntegrityReport> InspectAccountAsync(AccountId accountId, CancellationToken cancellationToken) =>
            inner.InspectAccountAsync(accountId, cancellationToken);

        public Task RecordViolationAsync(
            string runId,
            IntegrityMode mode,
            IntegrityFinding finding,
            CancellationToken cancellationToken) =>
            inner.RecordViolationAsync(runId, mode, finding, cancellationToken);

        public Task RecordRunAsync(string runId, IntegrityRunSummary summary, CancellationToken cancellationToken) =>
            inner.RecordRunAsync(runId, summary, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
