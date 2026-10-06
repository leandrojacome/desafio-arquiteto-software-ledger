using System.Diagnostics;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Abstractions;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class UnitOfWorkCancellationTests(PostgresFixture postgres)
{
    private const int RollbackDeadlineMilliseconds = 1000;
    private const int ReleaseTargetMilliseconds = 100;
    private const int PerformanceRepetitions = 5;
    private const int SessionsInFlight = 5;
    private const string WriteApplication = "ledger-api-write";

    [DockerFact]
    public async Task ACancelledToken_InsideTheFunction_ReleasesTheAccountLockWithinTheRollbackDeadline()
    {
        var elapsed = await CancelWhileHoldingTheLockAsync(RollbackDeadlineMilliseconds);

        elapsed.ShouldBeLessThanOrEqualTo(RollbackDeadlineMilliseconds);
    }

    [DockerFact]
    [Trait("Category", "Performance")]
    public async Task ACancelledToken_InsideTheFunction_ReleasesTheAccountLockInUnderOneHundredMillisecondsOnTheMedian()
    {
        var samples = new List<long>(PerformanceRepetitions);

        for (var repetition = 0; repetition < PerformanceRepetitions; repetition++)
        {
            samples.Add(await CancelWhileHoldingTheLockAsync(RollbackDeadlineMilliseconds));
        }

        samples.Order().ElementAt(PerformanceRepetitions / 2).ShouldBeLessThanOrEqualTo(ReleaseTargetMilliseconds);
    }

    [DockerFact]
    public async Task ATokenCancelledBeforeTheCommit_StillRollsBackAndReleasesTheLock()
    {
        await using var host = LedgerHost.Create(postgres);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateFundedAccountAsync(100.00m);
        await WarmUpRollbackAsync(host, accountId);
        using var cancellation = new CancellationTokenSource();

        var work = host.ExecuteAsync<int>(
            async (scope, token) =>
            {
                await ApplyDebitAsync(scope, accountId, token);
                await cancellation.CancelAsync();

                return 1;
            },
            cancellation.Token);

        await Should.ThrowAsync<OperationCanceledException>(() => work);

        (await IsLockedAsync(accountId)).ShouldBeFalse();
        (await queries.CountEntriesAsync(accountId)).ShouldBe(1);
        (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(100.00m);
    }

    [DockerFact]
    public async Task AFailureResultReturnedWithACancelledToken_StillReleasesTheLockAndTheConnectionIsReusable()
    {
        await using var host = LedgerHost.Create(postgres);
        var accountId = await host.CreateFundedAccountAsync(100.00m);
        using var cancellation = new CancellationTokenSource();

        var result = await host.ExecuteAsync<int>(
            async (scope, token) =>
            {
                await ApplyDebitAsync(scope, accountId, token);
                await cancellation.CancelAsync();

                return EntryErrors.InsufficientFunds;
            },
            cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        (await IsLockedAsync(accountId)).ShouldBeFalse();

        var next = await host.RegisterAsync(accountId, "after-the-cancellation", EntryType.Debit, 10.00m);

        next.IsSuccess.ShouldBeTrue();
    }

    [DockerFact]
    public async Task EachRequest_HoldsExactlyOneWriteSession_WhileItsFunctionRuns()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);
        var accounts = new List<AccountId>();

        for (var index = 0; index < SessionsInFlight; index++)
        {
            accounts.Add(await host.CreateFundedAccountAsync(100.00m));
        }

        var arrived = 0;
        var allArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var requests = accounts
            .Select(accountId => host.ExecuteAsync(
                async (scope, token) =>
                {
                    await ApplyDebitAsync(scope, accountId, token);
                    await scope.Accounts.GetForDiagnosisAsync(accountId, token);

                    if (Interlocked.Increment(ref arrived) == SessionsInFlight)
                    {
                        allArrived.SetResult();
                    }

                    await release.Task;

                    return Result.Success(true);
                }))
            .ToList();

        await allArrived.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var sessions = await WriteSessionsAsync();

        release.SetResult();
        await Task.WhenAll(requests);

        sessions.InTransaction.ShouldBe(SessionsInFlight);
        sessions.Total.ShouldBe(SessionsInFlight);
    }

    private async Task<long> CancelWhileHoldingTheLockAsync(int budgetMilliseconds)
    {
        await using var host = LedgerHost.Create(postgres);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateFundedAccountAsync(100.00m);
        await WarmUpRollbackAsync(host, accountId);
        using var cancellation = new CancellationTokenSource();
        var holdingTheLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var work = host.ExecuteAsync<int>(
            async (scope, token) =>
            {
                await ApplyDebitAsync(scope, accountId, token);
                holdingTheLock.SetResult();
                await Task.Delay(Timeout.Infinite, token);

                return 0;
            },
            cancellation.Token);

        var started = await Task.WhenAny(holdingTheLock.Task, work).WaitAsync(TimeSpan.FromSeconds(30));

        if (started == work)
        {
            await work;
        }

        (await IsLockedAsync(accountId)).ShouldBeTrue();

        var watch = Stopwatch.StartNew();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => work);
        var released = await WaitUntilFreeAsync(accountId, watch, budgetMilliseconds);

        released.ShouldBeTrue();
        (await queries.CountEntriesAsync(accountId)).ShouldBe(1);
        (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(100.00m);

        return watch.ElapsedMilliseconds;
    }

    private static async Task ApplyDebitAsync(IUnitOfWorkScope scope, AccountId accountId, CancellationToken token)
    {
        var entry = Entry.Debit(
            EntryId.From(Guid.CreateVersion7()).Value,
            accountId,
            Money.CreatePositive(1.00m, LedgerHost.Currency).Value,
            null,
            null,
            null).Value;

        var applied = await scope.Entries.TryApplyAsync(
            new NewEntry(entry, LedgerHost.ClientId, LedgerHost.CorrelationId),
            token);

        applied.IsSuccess.ShouldBeTrue();
    }

    private static async Task WarmUpRollbackAsync(LedgerHost host, AccountId accountId)
    {
        await host.ExecuteAsync<int>(
            async (scope, token) =>
            {
                await ApplyDebitAsync(scope, accountId, token);

                return EntryErrors.InsufficientFunds;
            });
    }

    private async Task<bool> WaitUntilFreeAsync(AccountId accountId, Stopwatch watch, int budgetMilliseconds)
    {
        while (watch.ElapsedMilliseconds <= budgetMilliseconds)
        {
            if (!await IsLockedAsync(accountId))
            {
                return true;
            }

            await Task.Delay(1);
        }

        return false;
    }

    private async Task<bool> IsLockedAsync(AccountId accountId)
    {
        await using var connection = await postgres.OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM account_balances WHERE account_id = @account_id FOR UPDATE NOWAIT",
            connection,
            transaction);

        command.Parameters.AddWithValue("account_id", accountId.Value);

        try
        {
            await command.ExecuteScalarAsync(CancellationToken.None);

            return false;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            return true;
        }
    }

    private async Task<SessionCount> WriteSessionsAsync()
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            """
            SELECT count(*), count(*) FILTER (WHERE state = 'idle in transaction')
            FROM pg_stat_activity
            WHERE application_name = @application AND usename = @role
            """);

        command.Parameters.AddWithValue("application", WriteApplication);
        command.Parameters.AddWithValue("role", PostgresFixture.ApiRole);

        await using var reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).ShouldBeTrue();

        return new SessionCount((int)reader.GetInt64(0), (int)reader.GetInt64(1));
    }

    private sealed record SessionCount(int Total, int InTransaction);
}
