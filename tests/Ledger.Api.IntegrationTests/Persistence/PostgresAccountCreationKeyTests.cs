using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Accounts;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresAccountCreationKeyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string ClientId = "pix-core";
    private const int HashSize = 32;

    private LedgerHost _host = null!;

    public Task InitializeAsync()
    {
        _host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [DockerFact]
    public async Task TryReserveCreationKeyAsync_WithAFreeKey_StoresTheClientTheHashAndTheAccount()
    {
        var account = NewAccount();
        var key = NewKey();

        var reserved = await CreateWithKeyAsync(account, ClientId, key, fill: 0x5A);

        reserved.ShouldBeTrue();

        var stored = await StoredKeyAsync(ClientId, key);

        stored.ShouldNotBeNull();
        stored.AccountId.ShouldBe(account.Id.Value);
        stored.RequestHash.ShouldBe(Hash(0x5A));
        stored.HashVersion.ShouldBe((short)1);
    }

    [DockerFact]
    public async Task TryReserveCreationKeyAsync_WithAKeyThatIsAlreadyTaken_ReturnsFalseAndKeepsTheFirstRow()
    {
        var first = NewAccount();
        var second = NewAccount();
        var key = NewKey();
        await CreateWithKeyAsync(first, ClientId, key, fill: 0x11);

        var reserved = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Flag(await scope.Accounts.TryReserveCreationKeyAsync(
                Reservation(second, ClientId, key, 0x22),
                token)));

        reserved.Value.ShouldBeFalse();

        var stored = await StoredKeyAsync(ClientId, key);

        stored.ShouldNotBeNull();
        stored.AccountId.ShouldBe(first.Id.Value);
        stored.RequestHash.ShouldBe(Hash(0x11));
        (await CountKeysAsync(key)).ShouldBe(1);
    }

    [DockerFact]
    public async Task TryReserveCreationKeyAsync_TheSameKeyForAnotherClient_IsAnotherReservation()
    {
        var key = NewKey();
        var first = NewAccount();
        var second = NewAccount();

        (await CreateWithKeyAsync(first, ClientId, key, 0x11)).ShouldBeTrue();
        (await CreateWithKeyAsync(second, "billing-core", key, 0x11)).ShouldBeTrue();

        (await CountKeysAsync(key)).ShouldBe(2);
        (await StoredKeyAsync("billing-core", key))!.AccountId.ShouldBe(second.Id.Value);
    }

    [DockerFact]
    public async Task FindCreationKeyAsync_ReturnsTheHashTheVersionTheAccountAndItsCreationInstant()
    {
        var account = NewAccount();
        var key = NewKey();
        await CreateWithKeyAsync(account, ClientId, key, fill: 0x33);

        var found = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(await scope.Accounts.FindCreationKeyAsync(ClientId, key, token)));
        var createdAt = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Created(await scope.Accounts.GetCreatedAtAsync(account.Id, token)));

        found.Record.ShouldNotBeNull();
        found.Record.AccountId.ShouldBe(account.Id);
        found.Record.HashVersion.ShouldBe(1);
        found.Record.RequestHash.ToArray().ShouldBe(Hash(0x33));
        found.Record.CreatedAt.ShouldBe(createdAt.At!.Value);
    }

    [DockerFact]
    public async Task FindCreationKeyAsync_ForAnotherClientOrAnUnknownKey_ReturnsNull()
    {
        var key = NewKey();
        await CreateWithKeyAsync(NewAccount(), ClientId, key, fill: 0x33);

        var otherClient = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(await scope.Accounts.FindCreationKeyAsync("billing-core", key, token)));
        var otherKey = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(await scope.Accounts.FindCreationKeyAsync(ClientId, NewKey(), token)));

        otherClient.Record.ShouldBeNull();
        otherKey.Record.ShouldBeNull();
    }

    [DockerFact]
    public async Task AReservationWithoutTheAccount_IsRefusedWhenTheTransactionCommits()
    {
        var account = NewAccount();
        var key = NewKey();

        var failure = await Should.ThrowAsync<PostgresException>(() => _host.InUnitOfWorkAsync(
            async (scope, token) => new Flag(await scope.Accounts.TryReserveCreationKeyAsync(
                Reservation(account, ClientId, key, 0x44),
                token))));

        failure.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        failure.ConstraintName.ShouldBe("fk_account_creation_keys_account_id");
        (await CountKeysAsync(key)).ShouldBe(0);
    }

    [DockerFact]
    public async Task ARolledBackReservation_FreesTheKeyAndLeavesNoAccount()
    {
        var account = NewAccount();
        var key = NewKey();

        var refused = await _host.ExecuteAsync<Flag>(async (scope, token) =>
        {
            await scope.Accounts.TryReserveCreationKeyAsync(Reservation(account, ClientId, key, 0x55), token);
            await scope.Accounts.CreateAsync(account, token);

            return AccountErrors.CreationKeyReused;
        });

        refused.IsFailure.ShouldBeTrue();
        (await CountKeysAsync(key)).ShouldBe(0);
        (await CountAccountsAsync(account.Id)).ShouldBe(0);

        var retried = await CreateWithKeyAsync(NewAccount(), ClientId, key, 0x55);

        retried.ShouldBeTrue();
    }

    [DockerFact]
    public async Task ConcurrentRequestsWithTheSameKey_ReserveItOnceAndCreateOneAccount()
    {
        const int requests = 8;
        var key = NewKey();
        var accounts = Enumerable.Range(0, requests).Select(_ => NewAccount()).ToList();

        var outcomes = await Task.WhenAll(accounts.Select(account => CreateWithKeyAsync(account, ClientId, key, 0x66)));

        outcomes.Count(reserved => reserved).ShouldBe(1);
        (await CountKeysAsync(key)).ShouldBe(1);

        var winner = (await StoredKeyAsync(ClientId, key))!.AccountId;
        var created = 0;

        foreach (var account in accounts)
        {
            created += (int)await CountAccountsAsync(account.Id);
        }

        created.ShouldBe(1);
        accounts.Single(account => account.Id.Value == winner).ShouldNotBeNull();
    }

    [DockerFact]
    public async Task ThePrivilegesOnTheKeys_FollowTheLeastPrivilegeOfEachRole()
    {
        await using var admin = await postgres.OpenConnectionAsync(CancellationToken.None);

        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_api', 'account_creation_keys', 'INSERT')")).ShouldBe(true);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_api', 'account_creation_keys', 'SELECT')")).ShouldBe(true);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_api', 'account_creation_keys', 'UPDATE')")).ShouldBe(false);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_api', 'account_creation_keys', 'DELETE')")).ShouldBe(false);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_worker', 'account_creation_keys', 'DELETE')")).ShouldBe(true);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_worker', 'account_creation_keys', 'INSERT')")).ShouldBe(false);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_worker', 'account_creation_keys', 'UPDATE')")).ShouldBe(false);
        (await ScalarAsync(admin, "SELECT has_column_privilege('ledger_worker', 'account_creation_keys', 'created_at', 'SELECT')")).ShouldBe(true);
        (await ScalarAsync(admin, "SELECT has_column_privilege('ledger_worker', 'account_creation_keys', 'request_hash', 'SELECT')")).ShouldBe(false);
        (await ScalarAsync(admin, "SELECT has_column_privilege('ledger_readonly', 'account_creation_keys', 'request_hash', 'SELECT')")).ShouldBe(false);
        (await ScalarAsync(admin, "SELECT has_column_privilege('ledger_readonly', 'account_creation_keys', 'account_id', 'SELECT')")).ShouldBe(true);
        (await ScalarAsync(admin, "SELECT has_table_privilege('ledger_readonly', 'account_creation_keys', 'INSERT')")).ShouldBe(false);
    }

    private static NewAccount NewAccount() =>
        new(
            AccountId.From(Guid.CreateVersion7()).Value,
            LedgerHost.Currency,
            Money.Create(0m, LedgerHost.Currency).Value,
            FakeProtectedDocument.Create(1));

    private static IdempotencyKey NewKey() => IdempotencyKey.From($"acct-{Guid.NewGuid():N}").Value;

    private static byte[] Hash(byte fill) => Enumerable.Repeat(fill, HashSize).ToArray();

    private static AccountCreationKeyReservation Reservation(
        NewAccount account,
        string clientId,
        IdempotencyKey key,
        byte fill) =>
        new(clientId, key, Hash(fill), 1, account.Id);

    private async Task<bool> CreateWithKeyAsync(NewAccount account, string clientId, IdempotencyKey key, byte fill)
    {
        var reserved = await _host.InUnitOfWorkAsync(async (scope, token) =>
        {
            var won = await scope.Accounts.TryReserveCreationKeyAsync(Reservation(account, clientId, key, fill), token);

            if (won)
            {
                (await scope.Accounts.CreateAsync(account, token)).ShouldNotBeNull();
            }

            return new Flag(won);
        });

        return reserved.Value;
    }

    private async Task<StoredKey?> StoredKeyAsync(string clientId, IdempotencyKey key)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            """
            SELECT account_id, request_hash, hash_version
            FROM account_creation_keys
            WHERE client_id = @client_id AND idempotency_key = @key
            """);

        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("key", key.Value);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        return await reader.ReadAsync(CancellationToken.None)
            ? new StoredKey(reader.GetGuid(0), (byte[])reader[1], reader.GetInt16(2))
            : null;
    }

    private async Task<long> CountKeysAsync(IdempotencyKey key)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT count(*) FROM account_creation_keys WHERE idempotency_key = @key");

        command.Parameters.AddWithValue("key", key.Value);

        return (long)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0L);
    }

    private async Task<long> CountAccountsAsync(AccountId accountId)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT count(*) FROM accounts WHERE id = @id");

        command.Parameters.AddWithValue("id", accountId.Value);

        return (long)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0L);
    }

    [SuppressMessage("Security", "CA2100", Justification = "Callers pass literal SQL.")]
    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        return await command.ExecuteScalarAsync(CancellationToken.None);
    }

    private sealed record Flag(bool Value);

    private sealed record Found(AccountCreationKeyRecord? Record);

    private sealed record Created(DateTimeOffset? At);

    private sealed record StoredKey(Guid AccountId, byte[] RequestHash, short HashVersion);
}
