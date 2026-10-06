using Ledger.Domain.Shared;

namespace Ledger.Application.Abstractions;

public interface IUnitOfWork
{
    Task<Result<TValue>> ExecuteAsync<TValue>(
        Func<IUnitOfWorkScope, CancellationToken, Task<Result<TValue>>> work,
        CancellationToken cancellationToken)
        where TValue : notnull;
}
