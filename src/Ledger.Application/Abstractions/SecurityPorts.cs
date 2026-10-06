using Ledger.Application.Accounts;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Abstractions;

public interface IKeyProvider
{
    bool IsAvailable { get; }

    KeySet Active { get; }

    IReadOnlyCollection<KeySet> Live { get; }

    IReadOnlyCollection<ushort> VanishedVersions { get; }

    KeySet Get(ushort version);
}

public interface IHolderDocumentProtector
{
    ProtectedHolderDocument Protect(HolderDocument document, AccountId accountId);

    Result<HolderDocument> Unprotect(ReadOnlyMemory<byte> encrypted, AccountId accountId);

    Result<ProtectedHolderDocument> Reprotect(ReadOnlyMemory<byte> encrypted, AccountId accountId);

    IReadOnlyList<ReadOnlyMemory<byte>> BlindIndexCandidates(HolderDocument document);
}

public interface IStatementCursorProtector
{
    string Protect(AccountId accountId, StatementPosition position);

    Result<StatementPosition> Unprotect(AccountId accountId, string cursor);
}

public interface IAccountKeyRewrapper
{
    Task<IAsyncDisposable?> TryBeginPassAsync(CancellationToken cancellationToken);

    Task<RewrapBatchResult> RewrapBatchAsync(
        RewrapBatchRequest request,
        Func<RewrapRow, Result<ProtectedHolderDocument>> transform,
        CancellationToken cancellationToken);

    Task<KeyUsage> ReadKeyUsageAsync(CancellationToken cancellationToken);
}
