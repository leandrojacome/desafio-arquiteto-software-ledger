using System.Security.Cryptography;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresIdempotencyStoreTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int HashVersion = 1;

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
    public async Task TryReserveAsync_FirstTimeOnAKey_ReturnsTrue()
    {
        var accountId = await _host.CreateAccountAsync();

        var reservation = await ReserveAndCommitAsync(accountId, "first-key");

        reservation.IsSuccess.ShouldBeTrue();
        reservation.Value.ShouldBeTrue();
    }

    [DockerFact]
    public async Task TryReserveAsync_AfterTheFirstCommitted_ReturnsFalse()
    {
        var accountId = await _host.CreateFundedAccountAsync(10.00m);
        await _host.RegisterAsync(accountId, "used-key", EntryType.Debit, 1.00m);

        var again = await _host.ExecuteAsync(
            async (scope, token) =>
            {
                var reservation = await scope.IdempotencyKeys.TryReserveAsync(
                    accountId,
                    Key("used-key"),
                    Hash(),
                    HashVersion,
                    EntryId.From(Guid.CreateVersion7()).Value,
                    token);

                scope.MarkForRollback();

                return reservation;
            });

        again.Value.ShouldBeFalse();
    }

    [DockerFact]
    public async Task TryReserveAsync_TheSameKeyInAnotherAccount_ReturnsTrue()
    {
        var first = await _host.CreateFundedAccountAsync(10.00m);
        var second = await _host.CreateFundedAccountAsync(10.00m);
        await _host.RegisterAsync(first, "shared-key", EntryType.Debit, 1.00m);

        var reservation = await RollbackAfterReservingAsync(second, "shared-key");

        reservation.Value.ShouldBeTrue();
    }

    [DockerFact]
    public async Task TryReserveAsync_ForAnUnknownAccount_FailsWithAccountNotFound()
    {
        var unknown = AccountId.From(Guid.CreateVersion7()).Value;

        var reservation = await RollbackAfterReservingAsync(unknown, "any-key");

        reservation.IsFailure.ShouldBeTrue();
        reservation.Error.ShouldBe(AccountErrors.NotFound);
    }

    [DockerFact]
    public async Task TryReserveAsync_ForAnUnknownAccount_LeavesNoErrorInTheDatabaseLog()
    {
        var unknown = AccountId.From(Guid.CreateVersion7()).Value;

        await RollbackAfterReservingAsync(unknown, "quiet-key");

        var log = await postgres.ServerLogAsync(CancellationToken.None);

        log.ShouldContain("ready to accept connections");
        log.ShouldNotContain(unknown.Value.ToString());
    }

    [DockerFact]
    public async Task TryReserveAsync_StoresTheHashWith32BytesAndTheVersion()
    {
        var accountId = await _host.CreateFundedAccountAsync(10.00m);
        var outcome = await _host.RegisterAsync(accountId, "stored-key", EntryType.Debit, 1.00m);

        outcome.IsSuccess.ShouldBeTrue();

        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT octet_length(request_hash), hash_version, entry_id FROM idempotency_keys WHERE account_id = @account_id AND idempotency_key = 'stored-key'");

        command.Parameters.AddWithValue("account_id", accountId.Value);

        await using var reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt32(0).ShouldBe(32);
        reader.GetInt16(1).ShouldBe((short)HashVersion);
        reader.GetGuid(2).ShouldBe(outcome.Value.Entry.Id.Value);
    }

    [DockerFact]
    public async Task Commit_AfterReservingAKeyWithoutRecordingTheEntry_FailsOnTheDeferredForeignKey()
    {
        var accountId = await _host.CreateAccountAsync();

        var failure = await Should.ThrowAsync<PostgresException>(() => _host.ExecuteAsync(
            async (scope, token) => await scope.IdempotencyKeys.TryReserveAsync(
                accountId,
                Key("orphan-key"),
                Hash(),
                HashVersion,
                EntryId.From(Guid.CreateVersion7()).Value,
                token)));

        failure.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        failure.ConstraintName.ShouldBe("fk_idempotency_keys_entry_id");
        (await _queries.CountKeysAsync(accountId)).ShouldBe(0);
    }

    [DockerFact]
    public async Task FindAsync_ReturnsTheHashTheVersionAndTheEntryOfTheFirstRequest()
    {
        var accountId = await _host.CreateFundedAccountAsync(100.00m);
        var first = await _host.RegisterAsync(accountId, "find-key", EntryType.Debit, 80.00m, description: "Conta de luz");

        var record = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(await scope.IdempotencyKeys.FindAsync(accountId, Key("find-key"), token)));

        record.Record.ShouldNotBeNull();
        record.Record.HashVersion.ShouldBe(HashVersion);
        record.Record.RequestHash.Length.ShouldBe(32);
        record.Record.Entry.ShouldBe(first.Value.Entry);
    }

    [DockerFact]
    public async Task FindAsync_ForAnUnknownKey_ReturnsNull()
    {
        var accountId = await _host.CreateAccountAsync();

        var record = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(await scope.IdempotencyKeys.FindAsync(accountId, Key("missing"), token)));

        record.Record.ShouldBeNull();
    }

    [DockerFact]
    public async Task FindAsync_ForAKeyOfAnotherAccount_ReturnsNull()
    {
        var owner = await _host.CreateFundedAccountAsync(10.00m);
        var other = await _host.CreateAccountAsync();
        await _host.RegisterAsync(owner, "owned-key", EntryType.Debit, 1.00m);

        var record = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(await scope.IdempotencyKeys.FindAsync(other, Key("owned-key"), token)));

        record.Record.ShouldBeNull();
    }

    [DockerFact]
    public async Task Register_WithAKeyWhoseRowIsThirtyFourDaysOld_StillReplays()
    {
        var accountId = await _host.CreateFundedAccountAsync(100.00m);
        var first = await _host.RegisterAsync(accountId, "old-key", EntryType.Debit, 10.00m);

        await using (var age = postgres.AdministrativeSource.CreateCommand(
                         "UPDATE idempotency_keys SET created_at = created_at - INTERVAL '34 days' WHERE account_id = @account_id AND idempotency_key = 'old-key'"))
        {
            age.Parameters.AddWithValue("account_id", accountId.Value);
            await age.ExecuteNonQueryAsync();
        }

        var again = await _host.RegisterAsync(accountId, "old-key", EntryType.Debit, 10.00m);

        again.Value.IsReplay.ShouldBeTrue();
        again.Value.Entry.ShouldBe(first.Value.Entry);
        (await _queries.CountEntriesAsync(accountId)).ShouldBe(2);
    }

    [DockerFact]
    public async Task TryReserveAsync_WhenTheFirstTransactionCommits_TheSecondWaitsAndThenReportsTheKeyAsTaken()
    {
        var accountId = await _host.CreateAccountAsync();

        await using var first = await Transaction.OpenAsync(_host);
        (await first.ReserveAsync(accountId, "contested")).Value.ShouldBeTrue();
        await first.RecordAnEntryAsync(accountId);

        await using var second = await Transaction.OpenAsync(_host);
        var waiting = second.ReserveAsync(accountId, "contested");

        await BlockedBackends.UntilAnyAsync(postgres, "the second reservation to wait for the first transaction");
        waiting.IsCompleted.ShouldBeFalse();

        await first.CommitAsync();

        (await waiting).Value.ShouldBeFalse();
    }

    [DockerFact]
    public async Task TryReserveAsync_WhenTheFirstTransactionRollsBack_TheSecondGetsTheKey()
    {
        var accountId = await _host.CreateAccountAsync();

        await using var first = await Transaction.OpenAsync(_host);
        (await first.ReserveAsync(accountId, "released")).Value.ShouldBeTrue();

        await using var second = await Transaction.OpenAsync(_host);
        var waiting = second.ReserveAsync(accountId, "released");

        await BlockedBackends.UntilAnyAsync(postgres, "the second reservation to wait for the first transaction");
        waiting.IsCompleted.ShouldBeFalse();

        await first.RollbackAsync();

        (await waiting).Value.ShouldBeTrue();
    }

    private static IdempotencyKey Key(string text) => IdempotencyKey.From(text).Value;

    private static byte[] Hash() => RandomNumberGenerator.GetBytes(32);

    private async Task<Result<bool>> ReserveAndCommitAsync(AccountId accountId, string key)
    {
        await using var transaction = await Transaction.OpenAsync(_host);
        var reservation = await transaction.ReserveAsync(accountId, key);

        if (reservation.IsSuccess && reservation.Value)
        {
            await transaction.RecordAnEntryAsync(accountId);
            await transaction.CommitAsync();
        }

        return reservation;
    }

    private async Task<Result<bool>> RollbackAfterReservingAsync(AccountId accountId, string key)
    {
        await using var transaction = await Transaction.OpenAsync(_host);

        return await transaction.ReserveAsync(accountId, key);
    }

    private sealed record Found(Ledger.Application.Abstractions.IdempotencyRecord? Record);

    private sealed class Transaction : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly NpgsqlTransaction _transaction;
        private readonly PostgresIdempotencyStore _store;
        private readonly PostgresEntryRepository _entries;
        private EntryId _entryId = EntryId.From(Guid.CreateVersion7()).Value;
        private bool _finished;

        private Transaction(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
            _store = new PostgresIdempotencyStore(connection, transaction);
            _entries = new PostgresEntryRepository(connection, transaction);
        }

        public static async Task<Transaction> OpenAsync(LedgerHost host)
        {
            var connection = await host.Connections.OpenConnectionAsync(PostgresSource.Write, CancellationToken.None);
            var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

            return new Transaction(connection, transaction);
        }

        public Task<Result<bool>> ReserveAsync(AccountId accountId, string key)
        {
            _entryId = EntryId.From(Guid.CreateVersion7()).Value;

            return _store.TryReserveAsync(accountId, Key(key), Hash(), HashVersion, _entryId, CancellationToken.None);
        }

        public async Task RecordAnEntryAsync(AccountId accountId)
        {
            var entry = Entry.Credit(
                _entryId,
                accountId,
                Money.CreatePositive(1.00m, LedgerHost.Currency).Value,
                null,
                null,
                null).Value;

            var applied = await _entries.TryApplyAsync(
                new NewEntry(entry, LedgerHost.ClientId, LedgerHost.CorrelationId),
                CancellationToken.None);

            applied.IsSuccess.ShouldBeTrue();
        }

        public async Task CommitAsync()
        {
            await _transaction.CommitAsync(CancellationToken.None);
            _finished = true;
        }

        public async Task RollbackAsync()
        {
            await _transaction.RollbackAsync(CancellationToken.None);
            _finished = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_finished)
            {
                await _transaction.RollbackAsync(CancellationToken.None);
            }

            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
