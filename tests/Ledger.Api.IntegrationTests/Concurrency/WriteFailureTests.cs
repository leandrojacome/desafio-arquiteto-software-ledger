using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Resilience")]
[Trait("Category", "Integration")]
public sealed class WriteFailureTests(PostgresFixture postgres)
{
    private const int EntryCount = 200;
    private const int MaxAttempts = 50;
    private const int TargetTerminations = 3;
    private const int InFlightPollMilliseconds = 2;
    private const int ShortLockTimeoutMilliseconds = 500;

    [DockerFact]
    public async Task DatabaseConnectionsKilledMidTraffic_LeaveNoPartialWrites_AndRetriesWithTheSameKeyDoNotDuplicate()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateFundedAccountAsync(1_000.00m);
        var callerRetries = 0;

        var traffic = ParallelGate.RunAsync(
            EntryCount,
            index => RegisterUntilItSucceedsAsync(host, accountId, $"killed-{index}", () => Interlocked.Increment(ref callerRetries)));

        var terminations = await TerminateConnectionsWhileTrafficIsInFlightAsync(traffic);
        var outcomes = await traffic;

        terminations.ShouldBeGreaterThan(0, "the traffic ended before any in-flight connection could be terminated");
        outcomes.ShouldAllBe(outcome => outcome.IsSuccess);
        (await queries.CountEntriesAsync(accountId)).ShouldBe(EntryCount + 1);
        (await queries.CountOutboxAsync(accountId)).ShouldBe(EntryCount + 1);
        (await queries.CountKeysAsync(accountId)).ShouldBe(EntryCount + 1);
        (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(1_000.00m - EntryCount);
        await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
    }

    [DockerFact]
    public async Task LockHeldLongerThanTheTimeout_FailsWithoutATrace_AndTheRepeatedKeyCreatesASingleEntry()
    {
        var overrides = new Dictionary<string, string?>
        {
            ["Postgres:Sources:Write:LockTimeoutMs"] = ShortLockTimeoutMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        await using var host = LedgerHost.Create(postgres, overrides);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateFundedAccountAsync(100.00m);

        await using (var holder = await postgres.OpenConnectionAsync(CancellationToken.None))
        {
            await using var transaction = await holder.BeginTransactionAsync(CancellationToken.None);
            await HoldBalanceRowAsync(holder, transaction, accountId);

            var failure = await Should.ThrowAsync<PostgresException>(
                () => host.RegisterAsync(accountId, "locked-key", EntryType.Debit, 10.00m));

            failure.SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable);
            await transaction.RollbackAsync(CancellationToken.None);
        }

        (await queries.CountEntriesAsync(accountId)).ShouldBe(1);
        (await queries.CountKeysAsync(accountId)).ShouldBe(1);
        (await queries.CountOutboxAsync(accountId)).ShouldBe(1);
        host.Logs.Events.Count(log => log.EventId == 1007).ShouldBe(1);
        host.Logs.Events.Count(log => log.EventId == 1006).ShouldBe(0);

        var first = await host.RegisterAsync(accountId, "locked-key", EntryType.Debit, 10.00m);
        var second = await host.RegisterAsync(accountId, "locked-key", EntryType.Debit, 10.00m);

        first.Value.IsReplay.ShouldBeFalse();
        second.Value.IsReplay.ShouldBeTrue();
        second.Value.Entry.Id.ShouldBe(first.Value.Entry.Id);
        (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(90.00m);
    }

    [DockerFact]
    public async Task OutboxTableLocked_DelaysTheEntryUntilItIsReleased_AndTheEntryIsRecordedOnce()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateFundedAccountAsync(100.00m);

        await using var holder = await postgres.OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await holder.BeginTransactionAsync(CancellationToken.None);

        await using (var lockTable = new NpgsqlCommand("LOCK TABLE outbox_messages IN ACCESS EXCLUSIVE MODE", holder, transaction))
        {
            await lockTable.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var pending = host.RegisterAsync(accountId, "outbox-locked", EntryType.Debit, 10.00m);

        await WaitForALockWaiterAsync();
        pending.IsCompleted.ShouldBeFalse();

        await transaction.RollbackAsync(CancellationToken.None);

        var outcome = await pending;

        outcome.IsSuccess.ShouldBeTrue();
        outcome.Value.IsReplay.ShouldBeFalse();
        (await queries.CountEntriesAsync(accountId)).ShouldBe(2);
        (await queries.CountOutboxAsync(accountId)).ShouldBe(2);
        (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(90.00m);
    }

    [DockerFact]
    public async Task ABrokenVersionCounter_ProducesAnInternalErrorLoggedWithTheConstraintName()
    {
        await using var host = LedgerHost.Create(postgres);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateFundedAccountAsync(100.00m);
        await host.RegisterAsync(accountId, "first-debit", EntryType.Debit, 1.00m);

        await using (var tamper = postgres.AdministrativeSource.CreateCommand(
                         "UPDATE account_balances SET version = 0 WHERE account_id = @account_id"))
        {
            tamper.Parameters.AddWithValue("account_id", accountId.Value);
            await tamper.ExecuteNonQueryAsync();
        }

        var failure = await Should.ThrowAsync<PostgresException>(
            () => host.RegisterAsync(accountId, "second-debit", EntryType.Debit, 1.00m));

        failure.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        failure.ConstraintName.ShouldBe("uq_ledger_entries_account_id_account_version");
        failure.Detail.ShouldNotBeNull().ShouldStartWith("Detail redacted");

        var logged = host.Logs.Events.Single(log => log.EventId == 1008);

        logged.Level.ShouldBe(LogLevel.Error);
        logged.Properties["ConstraintName"].ShouldBe("uq_ledger_entries_account_id_account_version");
        (await queries.CountEntriesAsync(accountId)).ShouldBe(2);
        (await queries.CountKeysAsync(accountId)).ShouldBe(2);
    }

    private static async Task<Result<EntryOutcome>> RegisterUntilItSucceedsAsync(
        LedgerHost host,
        AccountId accountId,
        string key,
        Action onRetry)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                return await host.RegisterAsync(accountId, key, EntryType.Debit, 1.00m);
            }
            catch (NpgsqlException exception) when (exception.IsTransient)
            {
                onRetry();
                await Task.Delay(20);
            }
        }

        throw new InvalidOperationException($"The entry with key {key} was not recorded after {MaxAttempts} attempts.");
    }

    private static async Task HoldBalanceRowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AccountId accountId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM account_balances WHERE account_id = @account_id FOR UPDATE",
            connection,
            transaction);

        command.Parameters.AddWithValue("account_id", accountId.Value);

        await command.ExecuteScalarAsync(CancellationToken.None);
    }

    private async Task WaitForALockWaiterAsync()
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            await using var command = postgres.AdministrativeSource.CreateCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE application_name = 'ledger-api-write' AND wait_event_type = 'Lock'");

            if ((long)(await command.ExecuteScalarAsync() ?? 0L) > 0)
            {
                return;
            }

            await Task.Delay(InFlightPollMilliseconds);
        }

        throw new InvalidOperationException("No write session ever waited for a lock.");
    }

    private async Task<int> TerminateConnectionsWhileTrafficIsInFlightAsync(Task traffic)
    {
        var terminations = 0;

        while (terminations < TargetTerminations && !traffic.IsCompleted)
        {
            if (await TerminateInFlightConnectionsAsync() > 0)
            {
                terminations++;
            }
            else
            {
                await Task.Delay(InFlightPollMilliseconds);
            }
        }

        return terminations;
    }

    private async Task<long> TerminateInFlightConnectionsAsync()
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE usename = @role AND application_name = 'ledger-api-write' AND pid <> pg_backend_pid() AND state IN ('active', 'idle in transaction')");

        command.Parameters.AddWithValue("role", PostgresFixture.ApiRole);

        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }
}
