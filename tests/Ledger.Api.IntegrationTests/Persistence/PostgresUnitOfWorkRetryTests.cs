using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Abstractions;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresUnitOfWorkRetryTests(PostgresFixture postgres)
{
    private const string Severity = "ERROR";

    [DockerFact]
    public async Task ExecuteAsync_WithATransientFailureTwice_RunsThreeTimesAndSucceeds()
    {
        var time = new FakeTimeProvider();
        await using var host = LedgerHost.Create(postgres, time: time);
        var executions = 0;

        var result = await TimePump.RunAsync(
            time,
            host.ExecuteAsync(
                (_, _) =>
                {
                    var attempt = Interlocked.Increment(ref executions);

                    return attempt < 3
                        ? throw Failure(PostgresErrorCodes.SerializationFailure)
                        : Task.FromResult<Result<int>>(attempt);
                }));

        executions.ShouldBe(3);
        result.Value.ShouldBe(3);
    }

    [DockerFact]
    public async Task ExecuteAsync_WithATransientFailureOnEveryAttempt_GivesUpAfterTwoRetriesAndThrowsTheLastFailure()
    {
        var time = new FakeTimeProvider();
        await using var host = LedgerHost.Create(postgres, time: time);
        var executions = 0;

        var work = host.ExecuteAsync<int>(
            (_, _) =>
            {
                Interlocked.Increment(ref executions);

                throw Failure(PostgresErrorCodes.SerializationFailure);
            });

        var thrown = await Should.ThrowAsync<PostgresException>(() => TimePump.RunAsync(time, work));

        thrown.SqlState.ShouldBe(PostgresErrorCodes.SerializationFailure);
        executions.ShouldBe(3);
    }

    [DockerTheory]
    [InlineData(PostgresErrorCodes.LockNotAvailable)]
    [InlineData(PostgresErrorCodes.QueryCanceled)]
    [InlineData(PostgresErrorCodes.UniqueViolation)]
    [InlineData(PostgresErrorCodes.ReadOnlySqlTransaction)]
    public async Task ExecuteAsync_WithAFailureThatRepeatingDoesNotFix_RunsOnce(string sqlState)
    {
        await using var host = LedgerHost.Create(postgres);
        var executions = 0;

        await Should.ThrowAsync<PostgresException>(() => host.ExecuteAsync<int>(
            (_, _) =>
            {
                Interlocked.Increment(ref executions);

                throw Failure(sqlState);
            }));

        executions.ShouldBe(1);
    }

    [DockerFact]
    public async Task ExecuteAsync_WithABugInTheFunction_RunsOnce()
    {
        await using var host = LedgerHost.Create(postgres);
        var executions = 0;

        await Should.ThrowAsync<InvalidOperationException>(() => host.ExecuteAsync<int>(
            (_, _) =>
            {
                Interlocked.Increment(ref executions);

                throw new InvalidOperationException("bug");
            }));

        executions.ShouldBe(1);
    }

    [DockerFact]
    public async Task ExecuteAsync_WithAFailureResult_RunsOnceAndRollsBack()
    {
        await using var host = LedgerHost.Create(postgres);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateAccountAsync();
        var executions = 0;

        var result = await host.ExecuteAsync<bool>(
            async (scope, token) =>
            {
                Interlocked.Increment(ref executions);
                await ApplyCreditAsync(scope, accountId, token);

                return EntryErrors.InsufficientFunds;
            });

        result.IsFailure.ShouldBeTrue();
        executions.ShouldBe(1);
        (await queries.CountEntriesAsync(accountId)).ShouldBe(0);
    }

    [DockerFact]
    public async Task ExecuteAsync_WhenTheTokenIsCancelledDuringTheBackoff_StopsWithoutANewAttempt()
    {
        var time = new FakeTimeProvider();
        await using var host = LedgerHost.Create(postgres, time: time);
        using var cancellation = new CancellationTokenSource();
        var executions = 0;

        var work = host.ExecuteAsync<int>(
            (_, _) =>
            {
                Interlocked.Increment(ref executions);

                throw Failure(PostgresErrorCodes.DeadlockDetected);
            },
            cancellation.Token);

        while (Volatile.Read(ref executions) == 0)
        {
            await Task.Delay(5);
        }

        await cancellation.CancelAsync();
        time.Advance(TimeSpan.FromSeconds(5));

        await Should.ThrowAsync<OperationCanceledException>(() => work);
        executions.ShouldBe(1);
    }

    [DockerFact]
    public async Task ExecuteAsync_OnEachRetry_LogsEvent1006WithTheSqlStateAndCountsTheReason()
    {
        var time = new FakeTimeProvider();
        await using var host = LedgerHost.Create(postgres, time: time);
        using var retries = new MeterCapture("ledger.db.retries");
        var executions = 0;

        await TimePump.RunAsync(
            time,
            host.ExecuteAsync(
                (_, _) =>
                {
                    var attempt = Interlocked.Increment(ref executions);

                    return attempt switch
                    {
                        1 => throw Failure(PostgresErrorCodes.DeadlockDetected),
                        2 => throw new NpgsqlException("connection lost", new IOException("broken pipe")),
                        _ => Task.FromResult<Result<int>>(attempt)
                    };
                }));

        var logged = host.Logs.Events.Where(log => log.EventId == 1006).ToList();

        logged.Count.ShouldBe(2);
        logged.ShouldAllBe(log => log.Level == LogLevel.Warning);
        logged[0].Properties["SqlState"].ShouldBe(PostgresErrorCodes.DeadlockDetected);
        logged[0].Properties["Attempt"].ShouldBe(1);
        logged[1].Properties["SqlState"].ShouldBe("none");
        logged[1].Properties["Attempt"].ShouldBe(2);
        retries.Measurements.Select(measurement => measurement.Tags["reason"]?.ToString()).ShouldBe(["deadlock", "transient_connection"]);
    }

    [DockerFact]
    public async Task ExecuteAsync_OnEachAttempt_OpensANewTransaction()
    {
        var time = new FakeTimeProvider();
        await using var host = LedgerHost.Create(postgres, time: time);
        var transactions = new List<long>();

        await TimePump.RunAsync(
            time,
            host.ExecuteAsync(
                async (scope, token) =>
                {
                    transactions.Add(await TransactionIdAsync((PostgresUnitOfWorkScope)scope, token));

                    return transactions.Count < 2
                        ? throw Failure(PostgresErrorCodes.SerializationFailure)
                        : Result.Success(transactions.Count);
                }));

        transactions.Count.ShouldBe(2);
        transactions[1].ShouldNotBe(transactions[0]);
    }

    [DockerFact]
    public async Task ExecuteAsync_AMarkForRollbackInAnAttemptThatFails_DoesNotLeakIntoTheNextAttempt()
    {
        var time = new FakeTimeProvider();
        await using var host = LedgerHost.Create(postgres, time: time);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateAccountAsync();
        var executions = 0;

        var result = await TimePump.RunAsync(
            time,
            host.ExecuteAsync(
                async (scope, token) =>
                {
                    var attempt = Interlocked.Increment(ref executions);

                    if (attempt == 1)
                    {
                        scope.MarkForRollback();

                        throw Failure(PostgresErrorCodes.SerializationFailure);
                    }

                    return Result.Success(await ApplyCreditAsync(scope, accountId, token));
                }));

        result.IsSuccess.ShouldBeTrue();
        executions.ShouldBe(2);
        (await queries.CountEntriesAsync(accountId)).ShouldBe(1);
    }

    [DockerFact]
    public async Task ExecuteAsync_WithRetriesDisabled_FailsOnTheFirstTransientFailure()
    {
        var overrides = new Dictionary<string, string?> { ["Resilience:Retry:MaxRetryAttempts"] = "0" };
        await using var host = LedgerHost.Create(postgres, overrides);
        var executions = 0;

        await Should.ThrowAsync<PostgresException>(() => host.ExecuteAsync<int>(
            (_, _) =>
            {
                Interlocked.Increment(ref executions);

                throw Failure(PostgresErrorCodes.SerializationFailure);
            }));

        executions.ShouldBe(1);
    }

    [DockerFact]
    public async Task ExecuteAsync_WithALockTimeout_LogsEvent1007OnceAndDoesNotRetry()
    {
        await using var host = LedgerHost.Create(postgres);

        await Should.ThrowAsync<PostgresException>(() => host.ExecuteAsync<int>(
            (_, _) => throw Failure(PostgresErrorCodes.LockNotAvailable)));

        host.Logs.Events.Count(log => log.EventId == 1007).ShouldBe(1);
        host.Logs.Events.Count(log => log.EventId == 1006).ShouldBe(0);
    }

    [DockerFact]
    public async Task ExecuteAsync_WithAnIntegrityViolation_LogsEvent1008WithTheConstraintNameAndRethrows()
    {
        await using var host = LedgerHost.Create(postgres);
        var violation = new PostgresException(
            "violation",
            Severity,
            Severity,
            PostgresErrorCodes.UniqueViolation,
            constraintName: "uq_ledger_entries_account_id_account_version");

        var thrown = await Should.ThrowAsync<PostgresException>(() => host.ExecuteAsync<int>((_, _) => throw violation));

        thrown.ShouldBeSameAs(violation);

        var logged = host.Logs.Events.Single(log => log.EventId == 1008);

        logged.Level.ShouldBe(LogLevel.Error);
        logged.Properties["ConstraintName"].ShouldBe("uq_ledger_entries_account_id_account_version");
        logged.Properties["SqlState"].ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    private static PostgresException Failure(string sqlState) => new("failure", Severity, Severity, sqlState);

    private static async Task<bool> ApplyCreditAsync(IUnitOfWorkScope scope, AccountId accountId, CancellationToken token)
    {
        var entry = Entry.Credit(
            EntryId.From(Guid.CreateVersion7()).Value,
            accountId,
            Money.CreatePositive(10.00m, LedgerHost.Currency).Value,
            null,
            null,
            null).Value;

        var applied = await scope.Entries.TryApplyAsync(
            new NewEntry(entry, LedgerHost.ClientId, LedgerHost.CorrelationId),
            token);

        return applied.IsSuccess;
    }

    private static async Task<long> TransactionIdAsync(PostgresUnitOfWorkScope scope, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT txid_current()", scope.Connection, scope.Transaction);

        return (long)(await command.ExecuteScalarAsync(token) ?? 0L);
    }
}
