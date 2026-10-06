using Ledger.Domain.Accounts;

namespace Ledger.Application.Integrity;

public interface IIntegritySession : IAsyncDisposable
{
    Task<DateTimeOffset> GetDatabaseNowAsync(CancellationToken cancellationToken);

    Task<IntegrityRunRecord?> FindLastRunAsync(IntegrityMode mode, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountId>> ReadRecentAccountsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountId>> ReadAccountBatchAsync(
        AccountId? after,
        int size,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<HeadRow>> CheckHeadsAsync(
        IReadOnlyList<AccountId> accounts,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChainRow>> CheckChainAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken);

    Task<long> CountEntriesAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken);

    Task<AccountIntegrityReport> InspectAccountAsync(AccountId accountId, CancellationToken cancellationToken);

    Task RecordViolationAsync(
        string runId,
        IntegrityMode mode,
        IntegrityFinding finding,
        CancellationToken cancellationToken);

    Task RecordRunAsync(string runId, IntegrityRunSummary summary, CancellationToken cancellationToken);
}
