using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresStatementReaderTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Open = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    private LedgerHost _host = null!;
    private LedgerSeeder _seeder = null!;

    public Task InitializeAsync()
    {
        _host = LedgerHost.Create(postgres);
        _seeder = new LedgerSeeder(postgres);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [DockerFact]
    public async Task ReadPageAsync_WithoutFilters_ReturnsTheNewestEntriesFirst()
    {
        var accountId = await SeededAccountAsync();

        var page = await Reader().ReadPageAsync(accountId, StatementBounds.Resolve(null, null, null), 4, CancellationToken.None);

        page.Select(entry => entry.AccountVersion).ShouldBe([5L, 4L, 3L, 2L]);
    }

    [DockerFact]
    public async Task ReadPageAsync_WithAnUpperPosition_ReturnsOnlyWhatComesBeforeIt()
    {
        var accountId = await SeededAccountAsync();
        var cursor = new StatementPosition(Open.AddMinutes(5).AddTicks(10), 3);

        var page = await Reader().ReadPageAsync(accountId, StatementBounds.Resolve(null, null, cursor), 10, CancellationToken.None);

        page.Select(entry => entry.AccountVersion).ShouldBe([2L, 1L]);
    }

    [DockerFact]
    public async Task ReadPageAsync_WithFromAndTo_KeepsToExclusiveAndFromInclusive()
    {
        var accountId = await SeededAccountAsync();

        var bounds = StatementBounds.Resolve(Open, Open.AddMinutes(5).AddTicks(10), null);

        var page = await Reader().ReadPageAsync(accountId, bounds, 10, CancellationToken.None);

        page.Select(entry => entry.AccountVersion).ShouldBe([2L, 1L]);
    }

    [DockerFact]
    public async Task ReadPageAsync_WithToAtTheInstantOfAnEntry_ExcludesThatEntry()
    {
        var accountId = await SeededAccountAsync();

        var bounds = StatementBounds.Resolve(null, Open.AddMinutes(5), null);

        var page = await Reader().ReadPageAsync(accountId, bounds, 10, CancellationToken.None);

        page.Select(entry => entry.AccountVersion).ShouldBe([1L]);
    }

    [DockerFact]
    public async Task ReadPageAsync_WithFromAfterTheNewestEntry_ReturnsNothing()
    {
        var accountId = await SeededAccountAsync();

        var page = await Reader().ReadPageAsync(
            accountId,
            StatementBounds.Resolve(Open.AddDays(1), null, null),
            10,
            CancellationToken.None);

        page.ShouldBeEmpty();
    }

    [DockerFact]
    public async Task ReadPageAsync_ForAnAccountWithoutEntries_ReturnsAnEmptyList()
    {
        var accountId = await _host.CreateAccountAsync();

        var page = await Reader().ReadPageAsync(accountId, StatementBounds.Resolve(null, null, null), 10, CancellationToken.None);

        page.ShouldBeEmpty();
    }

    [DockerFact]
    public async Task ReadPageAsync_MapsEveryColumnOfTheEntry()
    {
        var accountId = await _host.CreateAccountAsync(overdraftLimit: 500.00m);
        var original = await _seeder.InsertAtAsync(
            accountId, 1, "DEBIT", 150.25m, -150.25m, Open.AddTicks(30), Open.AddDays(-1), "Conta de água", "ref-77");
        await _seeder.InsertAtAsync(
            accountId, 2, "CREDIT", 150.25m, 0m, Open.AddMinutes(1), null, null, null, original);

        var page = await Reader().ReadPageAsync(accountId, StatementBounds.Resolve(null, null, null), 10, CancellationToken.None);

        var reversal = page[0];
        var debit = page[1];

        debit.Id.Value.ShouldBe(original);
        debit.AccountId.ShouldBe(accountId);
        debit.Type.ShouldBe(EntryType.Debit);
        debit.Amount.Amount.ShouldBe(150.25m);
        debit.Amount.Currency.ShouldBe("BRL");
        debit.BalanceAfter.Amount.ShouldBe(-150.25m);
        debit.RecordedAt.ShouldBe(Open.AddTicks(30));
        debit.OccurredAt.ShouldBe(Open.AddDays(-1));
        debit.Description.ShouldBe("Conta de água");
        debit.Reference.ShouldBe("ref-77");
        debit.ReversesEntryId.ShouldBeNull();
        reversal.ReversesEntryId.ShouldBe(EntryId.From(original).Value);
        reversal.Description.ShouldBeNull();
        reversal.Reference.ShouldBeNull();
    }

    [DockerFact]
    public async Task ReadPageAsync_WithTwoEntriesAtTheSameInstant_OrdersThemByPositionDescending()
    {
        var accountId = await _host.CreateAccountAsync();
        await _seeder.InsertAtAsync(accountId, 1, "CREDIT", 1.00m, 1.00m, Open);
        await _seeder.InsertAtAsync(accountId, 2, "CREDIT", 1.00m, 2.00m, Open);
        await _seeder.InsertAtAsync(accountId, 3, "CREDIT", 1.00m, 3.00m, Open);

        var page = await Reader().ReadPageAsync(accountId, StatementBounds.Resolve(null, null, null), 10, CancellationToken.None);

        page.Select(entry => entry.AccountVersion).ShouldBe([3L, 2L, 1L]);
    }

    [DockerFact]
    public async Task ReadPageAsync_WalkedWithTheCursorOfTheLastRow_NeverRepeatsNorSkipsAnEntry()
    {
        var accountId = await _host.CreateAccountAsync();

        for (var version = 1; version <= 23; version++)
        {
            await _seeder.InsertAtAsync(accountId, version, "CREDIT", 1.00m, version, Open.AddSeconds(version / 2));
        }

        var seen = new List<long>();
        StatementPosition? cursor = null;

        while (true)
        {
            var page = await Reader().ReadPageAsync(
                accountId,
                StatementBounds.Resolve(null, null, cursor),
                6,
                CancellationToken.None);

            seen.AddRange(page.Take(5).Select(entry => entry.AccountVersion));

            if (page.Count < 6)
            {
                break;
            }

            var last = page[4];
            cursor = new StatementPosition(last.RecordedAt, last.AccountVersion);
        }

        seen.ShouldBe(Enumerable.Range(1, 23).Select(version => (long)version).Reverse());
    }

    [DockerFact]
    public async Task AccountExistsAsync_ReportsWhetherTheAccountIsThere()
    {
        var accountId = await _host.CreateAccountAsync();

        (await Reader().AccountExistsAsync(accountId, CancellationToken.None)).ShouldBeTrue();
        (await Reader().AccountExistsAsync(AccountId.From(Guid.CreateVersion7()).Value, CancellationToken.None))
            .ShouldBeFalse();
    }

    [DockerFact]
    public async Task ReadPageAsync_ThroughTheStatementSource_CannotWrite()
    {
        await using var connection = await _host.Connections.OpenConnectionAsync(PostgresSource.Statement, CancellationToken.None);
        await using var command = new NpgsqlCommand(
            "INSERT INTO accounts (id, currency) VALUES (gen_random_uuid(), 'BRL')",
            connection);

        var refused = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        refused.SqlState.ShouldBe(PostgresErrorCodes.ReadOnlySqlTransaction);
    }

    private PostgresStatementReader Reader() => new(_host.Connections, _host.Service<DbTelemetry>(), _host.Time);

    private async Task<AccountId> SeededAccountAsync()
    {
        var accountId = await _host.CreateAccountAsync();

        await _seeder.InsertAtAsync(accountId, 1, "CREDIT", 100.00m, 100.00m, Open);
        await _seeder.InsertAtAsync(accountId, 2, "DEBIT", 30.00m, 70.00m, Open.AddMinutes(5));
        await _seeder.InsertAtAsync(accountId, 3, "CREDIT", 50.00m, 120.00m, Open.AddMinutes(5).AddTicks(10));
        await _seeder.InsertAtAsync(accountId, 4, "DEBIT", 20.00m, 100.00m, Open.AddHours(1));
        await _seeder.InsertAtAsync(accountId, 5, "CREDIT", 5.00m, 105.00m, Open.AddHours(2));

        return accountId;
    }
}
