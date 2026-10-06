using Ledger.Application.Accounts;
using Ledger.Application.Security;
using Ledger.Domain.Shared;

namespace Ledger.Application.Abstractions;

public interface IAccountKeyRewrapper
{
    Task<IAsyncDisposable?> TryBeginPassAsync(CancellationToken cancellationToken);

    Task<RewrapBatchResult> RewrapBatchAsync(
        RewrapBatchRequest request,
        Func<RewrapRow, Result<ProtectedHolderDocument>> transform,
        CancellationToken cancellationToken);

    Task<KeyUsage> ReadKeyUsageAsync(CancellationToken cancellationToken);
}
