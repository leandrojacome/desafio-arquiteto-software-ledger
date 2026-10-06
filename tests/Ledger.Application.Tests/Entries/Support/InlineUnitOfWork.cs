using Ledger.Application.Abstractions;
using Ledger.Domain.Shared;
using NSubstitute;

namespace Ledger.Application.Tests.Entries.Support;

internal sealed class InlineUnitOfWork(int runs = 1) : IUnitOfWork
{
    public RecordingScope Scope { get; } = new();

    public int Executions { get; private set; }

    public int Commits { get; private set; }

    public int Rollbacks { get; private set; }

    public int Calls { get; private set; }

    public Action? BeforeExecute { get; set; }

    public async Task<Result<TValue>> ExecuteAsync<TValue>(
        Func<IUnitOfWorkScope, CancellationToken, Task<Result<TValue>>> work,
        CancellationToken cancellationToken)
        where TValue : notnull
    {
        Calls++;
        BeforeExecute?.Invoke();

        var result = await RunOnceAsync(work, cancellationToken);

        for (var run = 1; run < runs; run++)
        {
            result = await RunOnceAsync(work, cancellationToken);
        }

        return result;
    }

    private async Task<Result<TValue>> RunOnceAsync<TValue>(
        Func<IUnitOfWorkScope, CancellationToken, Task<Result<TValue>>> work,
        CancellationToken cancellationToken)
        where TValue : notnull
    {
        Executions++;
        Scope.ClearMark();

        try
        {
            var result = await work(Scope, cancellationToken);

            if (result.IsSuccess && !Scope.RollbackMarked)
            {
                Commits++;
            }
            else
            {
                Rollbacks++;
            }

            return result;
        }
        catch (Exception)
        {
            Rollbacks++;
            throw;
        }
    }
}

internal sealed class RecordingScope : IUnitOfWorkScope
{
    public IAccountRepository Accounts { get; } = Substitute.For<IAccountRepository>();

    public IEntryRepository Entries { get; } = Substitute.For<IEntryRepository>();

    public IIdempotencyStore IdempotencyKeys { get; } = Substitute.For<IIdempotencyStore>();

    public IOutbox Outbox { get; } = Substitute.For<IOutbox>();

    public IAuditTrail Audit { get; } = Substitute.For<IAuditTrail>();

    public bool RollbackMarked { get; private set; }

    public void MarkForRollback()
    {
        RollbackMarked = true;
    }

    public void ClearMark()
    {
        RollbackMarked = false;
    }
}
