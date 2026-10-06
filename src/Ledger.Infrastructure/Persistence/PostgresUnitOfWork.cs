using System.Data;
using Ledger.Application.Abstractions;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Persistence.Retry;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresUnitOfWork(
    IPostgresConnectionFactory connectionFactory,
    PostgresSourceSelection sources,
    WriteRetryPipeline retryPipeline,
    ISecurityTelemetry securityTelemetry,
    TimeProvider timeProvider,
    ILogger<PostgresUnitOfWork> logger) : IUnitOfWork
{
    private const string ConstraintViolationClass = "23";
    private const string UnknownConstraint = "unknown";

    private static readonly TimeSpan RollbackDeadline = TimeSpan.FromSeconds(1);

    public async Task<Result<TValue>> ExecuteAsync<TValue>(
        Func<IUnitOfWorkScope, CancellationToken, Task<Result<TValue>>> work,
        CancellationToken cancellationToken)
        where TValue : notnull
    {
        try
        {
            return await retryPipeline.ExecuteAsync(
                token => new ValueTask<Result<TValue>>(AttemptAsync(work, token)),
                cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            PersistenceLog.LockTimeoutExceeded(logger);

            throw;
        }
        catch (PostgresException exception) when (IsConstraintViolation(exception))
        {
            PersistenceLog.UnexpectedConstraintViolation(
                logger,
                exception.SqlState,
                exception.ConstraintName ?? UnknownConstraint);

            throw;
        }
    }

    private static bool IsConstraintViolation(PostgresException exception) =>
        exception.SqlState.StartsWith(ConstraintViolationClass, StringComparison.Ordinal);

    private async Task<Result<TValue>> AttemptAsync<TValue>(
        Func<IUnitOfWorkScope, CancellationToken, Task<Result<TValue>>> work,
        CancellationToken cancellationToken)
        where TValue : notnull
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            sources.WriteSource,
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var scope = new PostgresUnitOfWorkScope(connection, transaction, securityTelemetry);
        var committed = false;

        try
        {
            var result = await work(scope, cancellationToken);

            if (result.IsFailure || scope.IsRollbackRequested)
            {
                return result;
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            return result;
        }
        finally
        {
            if (!committed)
            {
                await RollbackAsync(transaction);
            }
        }
    }

    private async Task RollbackAsync(NpgsqlTransaction transaction)
    {
        using var deadline = new CancellationTokenSource(RollbackDeadline, timeProvider);

        try
        {
            await transaction.RollbackAsync(deadline.Token);
        }
        catch (Exception exception) when (exception is NpgsqlException or OperationCanceledException
                                              or InvalidOperationException)
        {
            PersistenceLog.RollbackFailed(logger, exception);
        }
    }
}
