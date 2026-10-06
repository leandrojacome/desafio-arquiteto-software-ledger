using Ledger.Application.Integrity;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Tests.Integrity;

internal sealed class FakeIntegritySession : IIntegritySession
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 1, 22, 5, 0, TimeSpan.Zero);

    public IntegrityRunRecord? LastRun { get; set; }

    public IReadOnlyList<AccountId> RecentAccounts { get; set; } = [];

    public Func<DateTimeOffset, DateTimeOffset, IReadOnlyList<AccountId>>? RecentAccountsFor { get; set; }

    public List<(DateTimeOffset Start, DateTimeOffset End)> RecentReads { get; } = [];

    public IReadOnlyList<AccountId> AllAccounts { get; set; } = [];

    public Func<IReadOnlyList<AccountId>, IReadOnlyList<HeadRow>> HeadRows { get; set; } = _ => [];

    public Func<DateTimeOffset, DateTimeOffset, IReadOnlyList<ChainRow>> ChainRows { get; set; } = (_, _) => [];

    public Func<DateTimeOffset, DateTimeOffset, long> EntryCount { get; set; } = (_, _) => 10;

    public Action? OnCheckChain { get; set; }

    public Action? OnCheckHeads { get; set; }

    public bool Disposed { get; private set; }

    public List<IReadOnlyList<AccountId>> HeadBatches { get; } = [];

    public List<(DateTimeOffset Start, DateTimeOffset End)> ChainSlices { get; } = [];

    public List<(AccountId? After, int Size)> BatchReads { get; } = [];

    public List<(string RunId, IntegrityMode Mode, IntegrityFinding Finding)> Violations { get; } = [];

    public List<(string RunId, IntegrityRunSummary Summary)> RunRecords { get; } = [];

    public List<IntegrityMode> LastRunLookups { get; } = [];

    public Task<DateTimeOffset> GetDatabaseNowAsync(CancellationToken cancellationToken) => Task.FromResult(Now);

    public Task<IntegrityRunRecord?> FindLastRunAsync(IntegrityMode mode, CancellationToken cancellationToken)
    {
        LastRunLookups.Add(mode);

        return Task.FromResult(LastRun);
    }

    public Task<IReadOnlyList<AccountId>> ReadRecentAccountsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        RecentReads.Add((start, end));

        return Task.FromResult(RecentAccountsFor?.Invoke(start, end) ?? RecentAccounts);
    }

    public Task<IReadOnlyList<AccountId>> ReadAccountBatchAsync(
        AccountId? after,
        int size,
        CancellationToken cancellationToken)
    {
        BatchReads.Add((after, size));

        var remaining = after is null
            ? AllAccounts
            : AllAccounts.SkipWhile(account => account != after).Skip(1).ToList();

        return Task.FromResult<IReadOnlyList<AccountId>>(remaining.Take(size).ToList());
    }

    public Task<IReadOnlyList<HeadRow>> CheckHeadsAsync(
        IReadOnlyList<AccountId> accounts,
        CancellationToken cancellationToken)
    {
        HeadBatches.Add(accounts);
        OnCheckHeads?.Invoke();

        return Task.FromResult(HeadRows(accounts));
    }

    public Task<IReadOnlyList<ChainRow>> CheckChainAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        ChainSlices.Add((start, end));
        OnCheckChain?.Invoke();

        return Task.FromResult(ChainRows(start, end));
    }

    public Task<long> CountEntriesAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken) =>
        Task.FromResult(EntryCount(start, end));

    public Func<AccountId, AccountIntegrityReport> InspectionFor { get; set; } =
        accountId => new AccountIntegrityReport(accountId, 0m, 0m, 0, []);

    public List<AccountId> Inspected { get; } = [];

    public Task<AccountIntegrityReport> InspectAccountAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        Inspected.Add(accountId);

        return Task.FromResult(InspectionFor(accountId));
    }

    public Task RecordViolationAsync(
        string runId,
        IntegrityMode mode,
        IntegrityFinding finding,
        CancellationToken cancellationToken)
    {
        Violations.Add((runId, mode, finding));

        return Task.CompletedTask;
    }

    public Task RecordRunAsync(string runId, IntegrityRunSummary summary, CancellationToken cancellationToken)
    {
        RunRecords.Add((runId, summary));

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;

        return ValueTask.CompletedTask;
    }
}
