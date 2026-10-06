using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Persistence;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresBalanceReaderTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Open = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    private LedgerHost _host = null!;
    private LedgerQueries _queries = null!;
    private LedgerSeeder _seeder = null!;

    public Task InitializeAsync()
    {
        _host = LedgerHost.Create(postgres);
        _queries = new LedgerQueries(postgres);
        _seeder = new LedgerSeeder(postgres);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [DockerFact]
    public async Task ReadCurrentAsync_ReturnsTheCurrencyTheBalanceTheLimitAndTheLastEntry()
    {
        var accountId = await _host.CreateFundedAccountAsync(1000.00m, overdraftLimit: 50.00m);
        var debit = await _host.RegisterAsync(accountId, "debit", EntryType.Debit, 150.00m);
        var before = await _queries.DatabaseNowAsync();

        var reading = await Reader().ReadCurrentAsync(accountId, CancellationToken.None);

        var after = await _queries.DatabaseNowAsync();

        reading.IsSuccess.ShouldBeTrue();
        reading.Value.Currency.ShouldBe("BRL");
        reading.Value.Balance.ShouldBe(850.00m);
        reading.Value.OverdraftLimit.ShouldBe(50.00m);
        reading.Value.LastEntryId.ShouldBe(debit.Value.Entry.Id);
        reading.Value.DatabaseNow.UtcDateTime.ShouldBeInRange(before, after);
    }

    [DockerFact]
    public async Task ReadCurrentAsync_ForAnAccountWithoutEntries_ReturnsZeroAndNoLastEntry()
    {
        var accountId = await _host.CreateAccountAsync(overdraftLimit: 10.00m);

        var reading = await Reader().ReadCurrentAsync(accountId, CancellationToken.None);

        reading.Value.Balance.ShouldBe(0m);
        reading.Value.OverdraftLimit.ShouldBe(10.00m);
        reading.Value.LastEntryId.ShouldBeNull();
    }

    [DockerFact]
    public async Task ReadCurrentAsync_ForAnUnknownAccount_ReturnsNotFound()
    {
        var reading = await Reader().ReadCurrentAsync(AccountId.From(Guid.CreateVersion7()).Value, CancellationToken.None);

        reading.IsFailure.ShouldBeTrue();
        reading.Error.ShouldBe(AccountErrors.NotFound);
    }

    [DockerFact]
    public async Task ReadCurrentAsync_WithANegativeBalance_KeepsTheTwoDecimalsOfBalanceAndLimit()
    {
        var accountId = await _host.CreateFundedAccountAsync(100.00m, overdraftLimit: 250.50m);
        await _host.RegisterAsync(accountId, "overdraw", EntryType.Debit, 300.25m);

        var reading = await Reader().ReadCurrentAsync(accountId, CancellationToken.None);

        reading.Value.Balance.ShouldBe(-200.25m);
        reading.Value.OverdraftLimit.ShouldBe(250.50m);
    }

    [DockerFact]
    public async Task ReadAtAsync_FollowsTheBalanceAfterOfTheLastEntryUpToTheInstant()
    {
        var accountId = await SeededAccountAsync();

        (await BalanceAt(accountId, Open.AddHours(-1))).ShouldBeNull();
        (await BalanceAt(accountId, Open)).ShouldBe(100.00m);
        (await BalanceAt(accountId, Open.AddMinutes(5).AddTicks(-10))).ShouldBe(100.00m);
        (await BalanceAt(accountId, Open.AddMinutes(5))).ShouldBe(70.00m);
        (await BalanceAt(accountId, Open.AddMinutes(5).AddTicks(10))).ShouldBe(120.00m);
        (await BalanceAt(accountId, Open.AddHours(1))).ShouldBe(100.00m);
        (await BalanceAt(accountId, Open.AddHours(2))).ShouldBe(105.00m);
        (await BalanceAt(accountId, Open.AddDays(1))).ShouldBe(105.00m);
    }

    [DockerFact]
    public async Task ReadAtAsync_ReturnsTheIdOfTheLastEntryAndTheCurrentLimit()
    {
        var accountId = await _host.CreateAccountAsync(overdraftLimit: 75.00m);
        var third = await _seeder.InsertAtAsync(accountId, 1, "CREDIT", 10.00m, 10.00m, Open);

        var reading = await Reader().ReadAtAsync(accountId, Open.AddSeconds(1), CancellationToken.None);

        reading.Value.LastEntryId.ShouldBe(EntryId.From(third).Value);
        reading.Value.OverdraftLimit.ShouldBe(75.00m);
        reading.Value.Currency.ShouldBe("BRL");
    }

    [DockerFact]
    public async Task ReadAtAsync_BeforeTheFirstEntry_ReturnsTheAccountWithoutBalance()
    {
        var accountId = await _host.CreateAccountAsync();

        var reading = await Reader().ReadAtAsync(accountId, Open, CancellationToken.None);

        reading.IsSuccess.ShouldBeTrue();
        reading.Value.BalanceAfter.ShouldBeNull();
        reading.Value.LastEntryId.ShouldBeNull();
    }

    [DockerFact]
    public async Task ReadAtAsync_ForAnUnknownAccount_ReturnsNotFound()
    {
        var reading = await Reader().ReadAtAsync(
            AccountId.From(Guid.CreateVersion7()).Value,
            Open,
            CancellationToken.None);

        reading.Error.ShouldBe(AccountErrors.NotFound);
    }

    [DockerFact]
    public async Task ReadAtAsync_WithTwoEntriesAtTheSameInstant_ReturnsTheOneWithTheHigherPosition()
    {
        var accountId = await _host.CreateAccountAsync();
        await _seeder.InsertAtAsync(accountId, 1, "CREDIT", 10.00m, 10.00m, Open);
        await _seeder.InsertAtAsync(accountId, 2, "CREDIT", 5.00m, 15.00m, Open);

        (await BalanceAt(accountId, Open)).ShouldBe(15.00m);
    }

    [DockerFact]
    public async Task ReadAtAsync_IgnoresTheBusinessDate()
    {
        var accountId = await _host.CreateAccountAsync();
        await _seeder.InsertAtAsync(accountId, 1, "CREDIT", 10.00m, 10.00m, Open, occurredAt: Open.AddYears(-1));
        await _seeder.InsertAtAsync(accountId, 2, "CREDIT", 5.00m, 15.00m, Open.AddHours(1), occurredAt: Open.AddYears(-2));

        (await BalanceAt(accountId, Open.AddMinutes(30))).ShouldBe(10.00m);
    }

    private PostgresBalanceReader Reader() => new(_host.Connections, _host.Service<DbTelemetry>(), _host.Time);

    private async Task<decimal?> BalanceAt(AccountId accountId, DateTimeOffset instant)
    {
        var reading = await Reader().ReadAtAsync(accountId, instant, CancellationToken.None);

        reading.IsSuccess.ShouldBeTrue();

        return reading.Value.BalanceAfter;
    }

    private async Task<AccountId> SeededAccountAsync()
    {
        var accountId = await _host.CreateAccountAsync();

        await _seeder.InsertAtAsync(accountId, 1, "CREDIT", 100.00m, 100.00m, Open, occurredAt: Open.AddDays(-2));
        await _seeder.InsertAtAsync(accountId, 2, "DEBIT", 30.00m, 70.00m, Open.AddMinutes(5));
        await _seeder.InsertAtAsync(accountId, 3, "CREDIT", 50.00m, 120.00m, Open.AddMinutes(5).AddTicks(10), occurredAt: Open.AddDays(-3));
        await _seeder.InsertAtAsync(accountId, 4, "DEBIT", 20.00m, 100.00m, Open.AddHours(1));
        await _seeder.InsertAtAsync(accountId, 5, "CREDIT", 5.00m, 105.00m, Open.AddHours(2));

        return accountId;
    }
}
