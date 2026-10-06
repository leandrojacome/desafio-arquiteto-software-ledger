using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Accounts;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresAccountRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int KeyVersion = 3;

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
    public async Task GetForDiagnosisAsync_ReturnsTheCurrencyTheBalanceAndTheLimit()
    {
        var accountId = await _host.CreateFundedAccountAsync(100.00m, overdraftLimit: 250.50m);
        await _host.RegisterAsync(accountId, "spend", EntryType.Debit, 30.00m);

        var diagnosis = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(await scope.Accounts.GetForDiagnosisAsync(accountId, token)));

        diagnosis.Balance.ShouldNotBeNull();
        diagnosis.Balance.Currency.ShouldBe("BRL");
        diagnosis.Balance.Amount.ShouldBe(70.00m);
        diagnosis.Balance.OverdraftLimit.ShouldBe(250.50m);
        diagnosis.Balance.AccountId.ShouldBe(accountId);
    }

    [DockerFact]
    public async Task GetForDiagnosisAsync_ForAnAccountWithoutEntries_ReturnsAZeroBalance()
    {
        var accountId = await _host.CreateAccountAsync();

        var diagnosis = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(await scope.Accounts.GetForDiagnosisAsync(accountId, token)));

        diagnosis.Balance.ShouldNotBeNull();
        diagnosis.Balance.Amount.ShouldBe(0m);
    }

    [DockerFact]
    public async Task GetForDiagnosisAsync_ForAnAccountThatDoesNotExist_ReturnsNull()
    {
        var missing = AccountId.From(Guid.CreateVersion7()).Value;

        var diagnosis = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(await scope.Accounts.GetForDiagnosisAsync(missing, token)));

        diagnosis.Balance.ShouldBeNull();
    }

    [DockerFact]
    public async Task CreateAsync_StoresTheAccountAndAZeroBalanceRowAtTheCreationInstant()
    {
        var account = NewAccount(1000.00m);

        var createdAt = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Created(await scope.Accounts.CreateAsync(account, token)));

        var balance = await _queries.BalanceRowAsync(account.Id);

        createdAt.At.ShouldNotBeNull();
        balance.Balance.ShouldBe(0m);
        balance.Version.ShouldBe(0);
        balance.LastEntryId.ShouldBeNull();
        balance.OverdraftLimit.ShouldBe(1000.00m);
        balance.LastRecordedAt.ShouldBe(createdAt.At.Value.UtcDateTime);
    }

    [DockerFact]
    public async Task CreateAsync_StoresTheProtectedDocumentColumns()
    {
        var account = NewAccount(0m);

        await _host.InUnitOfWorkAsync(
            async (scope, token) => new Created(await scope.Accounts.CreateAsync(account, token)));

        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT currency, holder_document_encrypted, holder_document_blind_index, holder_document_key_version FROM accounts WHERE id = @id");

        command.Parameters.AddWithValue("id", account.Id.Value);

        await using var reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("BRL");
        ((byte[])reader[1]).ShouldBe(account.Document.Encrypted.ToArray());
        ((byte[])reader[2]).ShouldBe(account.Document.BlindIndex.ToArray());
        reader.GetInt32(3).ShouldBe(KeyVersion);
    }

    [DockerFact]
    public async Task CreateAsync_WithAnIdThatAlreadyExists_ReturnsNullAndKeepsTheTransactionUsable()
    {
        var account = NewAccount(0m);
        await _host.InUnitOfWorkAsync(
            async (scope, token) => new Created(await scope.Accounts.CreateAsync(account, token)));

        var repeated = await _host.InUnitOfWorkAsync(
            async (scope, token) =>
            {
                var second = await scope.Accounts.CreateAsync(account, token);
                var readBack = await scope.Accounts.GetCreatedAtAsync(account.Id, token);

                return new Repeated(second, readBack);
            });

        repeated.Second.ShouldBeNull();
        repeated.ReadBack.ShouldNotBeNull();
        (await _queries.BalanceRowCountAsync(account.Id)).ShouldBe(1);
    }

    [DockerFact]
    public async Task GetCreatedAtAsync_ReturnsTheCreationInstantOfTheAccount()
    {
        var account = NewAccount(0m);
        var created = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Created(await scope.Accounts.CreateAsync(account, token)));

        var readBack = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Created(await scope.Accounts.GetCreatedAtAsync(account.Id, token)));

        readBack.At.ShouldBe(created.At);
    }

    [DockerFact]
    public async Task GetCreatedAtAsync_ForAnUnknownAccount_ReturnsNull()
    {
        var missing = AccountId.From(Guid.CreateVersion7()).Value;

        var readBack = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Created(await scope.Accounts.GetCreatedAtAsync(missing, token)));

        readBack.At.ShouldBeNull();
    }

    private static NewAccount NewAccount(decimal overdraftLimit)
    {
        var document = FakeProtectedDocument.Create(KeyVersion);

        return new NewAccount(
            AccountId.From(Guid.CreateVersion7()).Value,
            LedgerHost.Currency,
            Money.Create(overdraftLimit, LedgerHost.Currency).Value,
            document);
    }

    private sealed record Found(AccountBalance? Balance);

    private sealed record Created(DateTimeOffset? At);

    private sealed record Repeated(DateTimeOffset? Second, DateTimeOffset? ReadBack);
}
