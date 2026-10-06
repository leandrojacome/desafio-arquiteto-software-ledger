using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Abstractions;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresUnitOfWorkTests(PostgresFixture postgres) : IAsyncLifetime
{
    private LedgerHost _host = null!;
    private LedgerQueries _queries = null!;

    public Task InitializeAsync()
    {
        _host = LedgerHost.Create(postgres);
        _queries = new LedgerQueries(postgres);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [DockerFact]
    public async Task ExecuteAsync_WhenTheFunctionSucceeds_CommitsAndTheEffectIsVisibleToAnotherConnection()
    {
        var accountId = await _host.CreateAccountAsync();

        var result = await _host.ExecuteAsync(
            async (scope, token) => await Credit(scope, accountId, 25.00m, token));

        result.IsSuccess.ShouldBeTrue();
        (await _queries.BalanceRowAsync(accountId)).Balance.ShouldBe(25.00m);
        (await _queries.CountEntriesAsync(accountId)).ShouldBe(1);
    }

    [DockerFact]
    public async Task ExecuteAsync_WhenTheFunctionReturnsAFailure_RollsBackAndReturnsTheFailure()
    {
        var accountId = await _host.CreateAccountAsync();

        var result = await _host.ExecuteAsync<bool>(
            async (scope, token) =>
            {
                await Credit(scope, accountId, 25.00m, token);

                return EntryErrors.InsufficientFunds;
            });

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EntryErrors.InsufficientFunds);
        (await _queries.BalanceRowAsync(accountId)).Balance.ShouldBe(0m);
        (await _queries.CountEntriesAsync(accountId)).ShouldBe(0);
    }

    [DockerFact]
    public async Task ExecuteAsync_WhenTheFunctionMarksTheRollback_RollsBackButStillReturnsTheSuccess()
    {
        var accountId = await _host.CreateAccountAsync();

        var result = await _host.ExecuteAsync(
            async (scope, token) =>
            {
                var credited = await Credit(scope, accountId, 25.00m, token);
                scope.MarkForRollback();

                return credited;
            });

        result.IsSuccess.ShouldBeTrue();
        (await _queries.CountEntriesAsync(accountId)).ShouldBe(0);
        (await _queries.BalanceRowAsync(accountId)).Version.ShouldBe(0);
    }

    [DockerFact]
    public async Task ExecuteAsync_WhenTheFunctionThrows_RollsBackAndRethrowsTheSameException()
    {
        var accountId = await _host.CreateAccountAsync();
        var failure = new InvalidOperationException("boom");

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => _host.ExecuteAsync<bool>(
            async (scope, token) =>
            {
                await Credit(scope, accountId, 25.00m, token);

                throw failure;
            }));

        thrown.ShouldBeSameAs(failure);
        (await _queries.CountEntriesAsync(accountId)).ShouldBe(0);
    }

    [DockerFact]
    public async Task ExecuteAsync_RunsInReadCommittedOnTheWriteSource()
    {
        var observed = await _host.InUnitOfWorkAsync(
            async (scope, token) =>
            {
                var internals = (PostgresUnitOfWorkScope)scope;

                return new Observation(
                    await ShowAsync(internals, "transaction_isolation", token),
                    await ShowAsync(internals, "application_name", token),
                    await ShowAsync(internals, "lock_timeout", token),
                    await ShowAsync(internals, "default_transaction_read_only", token));
            });

        observed.Isolation.ShouldBe("read committed");
        observed.ApplicationName.ShouldBe("ledger-api-write");
        observed.LockTimeout.ShouldBe("1s");
        observed.ReadOnly.ShouldBe("off");
    }

    [DockerFact]
    public async Task ExecuteAsync_RunsTheWholeFunctionOnASingleSession()
    {
        var accountId = await _host.CreateAccountAsync();

        var sessions = await _host.InUnitOfWorkAsync(
            async (scope, token) =>
            {
                var internals = (PostgresUnitOfWorkScope)scope;
                var first = await ShowPidAsync(internals, token);

                await Credit(scope, accountId, 5.00m, token);
                await scope.Accounts.GetForDiagnosisAsync(accountId, token);

                return new Sessions(first, await ShowPidAsync(internals, token));
            });

        sessions.Before.ShouldBe(sessions.After);
    }

    private static async Task<Result<bool>> Credit(
        IUnitOfWorkScope scope,
        AccountId accountId,
        decimal amount,
        CancellationToken token)
    {
        var entry = Entry.Credit(
            EntryId.From(Guid.CreateVersion7()).Value,
            accountId,
            Money.CreatePositive(amount, LedgerHost.Currency).Value,
            null,
            null,
            null).Value;

        var applied = await scope.Entries.TryApplyAsync(
            new NewEntry(entry, LedgerHost.ClientId, LedgerHost.CorrelationId),
            token);

        return applied.IsSuccess;
    }

    private static async Task<string> ShowAsync(PostgresUnitOfWorkScope scope, string setting, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT current_setting(@setting)", scope.Connection, scope.Transaction);

        command.Parameters.AddWithValue("setting", setting);

        return (string)(await command.ExecuteScalarAsync(token) ?? string.Empty);
    }

    private static async Task<int> ShowPidAsync(PostgresUnitOfWorkScope scope, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT pg_backend_pid()", scope.Connection, scope.Transaction);

        return (int)(await command.ExecuteScalarAsync(token) ?? 0);
    }

    private sealed record Observation(string Isolation, string ApplicationName, string LockTimeout, string ReadOnly);

    private sealed record Sessions(int Before, int After);
}
