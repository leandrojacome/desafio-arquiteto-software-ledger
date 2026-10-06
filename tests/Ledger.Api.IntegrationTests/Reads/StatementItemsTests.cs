using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Entries;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class StatementItemsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string[] ContractFields =
    [
        "entryId", "accountId", "accountVersion", "type", "amount", "currency", "balanceAfter", "occurredAt",
        "recordedAt", "reversesEntryId", "description", "reference"
    ];

    private ReadWorld _world = null!;

    public Task InitializeAsync()
    {
        _world = ReadWorld.Create(postgres);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [DockerFact]
    public async Task Statement_AfterADebitAndItsReversal_ShowsTheReversalAndKeepsTheOriginalIntact()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(1000.00m);
        var debit = await _world.Host.RegisterAsync(accountId, "debit-80", EntryType.Debit, 80.00m);

        using var before = await _world.Client.StatementAsync(accountId);
        var originalBefore = StatementItem.ItemsOf(before).Single(item => item.EntryId == debit.Value.Entry.Id.ToString());

        var reversal = await _world.Host.ReverseAsync(accountId, debit.Value.Entry.Id, "reverse-80");

        using var after = await _world.Client.StatementAsync(accountId);
        var items = StatementItem.ItemsOf(after);
        var originalAfter = items.Single(item => item.EntryId == debit.Value.Entry.Id.ToString());
        var reversalItem = items.Single(item => item.EntryId == reversal.Value.Entry.Id.ToString());

        reversal.IsSuccess.ShouldBeTrue();
        originalAfter.ShouldBe(originalBefore);
        originalAfter.Amount.ShouldBe(80.00m);
        originalAfter.BalanceAfter.ShouldBe(920.00m);
        originalAfter.RecordedAt.ShouldBe(originalBefore.RecordedAt);
        reversalItem.ReversesEntryId.ShouldBe(debit.Value.Entry.Id.ToString());
        reversalItem.Type.ShouldBe("CREDIT");
        reversalItem.BalanceAfter.ShouldBe(1000.00m);
        items[0].EntryId.ShouldBe(reversalItem.EntryId);
        items.Count.ShouldBe(3);
    }

    [DockerFact]
    public async Task Statement_Item_HasExactlyTheTwelveFieldsOfTheEntryContract()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(50.00m);

        using var response = await _world.Client.StatementAsync(accountId);

        var item = response.Json.GetProperty("items").EnumerateArray().Single();

        item.EnumerateObject().Select(property => property.Name).ShouldBe(ContractFields);
    }

    [DockerFact]
    public async Task Statement_Items_MatchTheStoredRowsFieldByField()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(500.00m);
        await _world.Host.RegisterAsync(
            accountId,
            "with-text",
            EntryType.Debit,
            12.34m,
            description: "Pagamento à vista, conciliação ÇÃO",
            reference: "REF-2026-0001");

        var rows = await _world.Queries.EntriesAsync(accountId);

        using var response = await _world.Client.StatementAsync(accountId);

        var items = StatementItem.ItemsOf(response);

        items.Count.ShouldBe(rows.Count);

        foreach (var (item, row) in items.Zip(rows.OrderByDescending(entry => entry.AccountVersion)))
        {
            item.EntryId.ShouldBe(row.Id.ToString());
            item.AccountVersion.ShouldBe(row.AccountVersion);
            item.Type.ShouldBe(row.Type);
            item.Amount.ShouldBe(row.Amount);
            item.BalanceAfter.ShouldBe(row.BalanceAfter);
            item.RecordedAt.ShouldBe(StatementItem.FormatInstant(new DateTimeOffset(row.RecordedAt, TimeSpan.Zero)));
            item.OccurredAt.ShouldBe(StatementItem.FormatInstant(new DateTimeOffset(row.OccurredAt, TimeSpan.Zero)));
            item.ReversesEntryId.ShouldBe(row.ReversesEntryId?.ToString());
            item.Description.ShouldBe(row.Description);
            item.Reference.ShouldBe(row.Reference);
        }

        items[0].Description.ShouldBe("Pagamento à vista, conciliação ÇÃO");
        items[0].Reference.ShouldBe("REF-2026-0001");
        items[1].Description.ShouldBeNull();
        items[1].Reference.ShouldBeNull();
    }

    [DockerFact]
    public async Task Statement_ItemOfAnEntryWrittenThroughTheApi_IsIdenticalToTheBodyOfThe201()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        using var writer = ReadWriteClient.For(_world.Factory);

        using var created = await writer.PostEntryAsync(
            accountId,
            "statement-vs-201",
            "CREDIT",
            "150.25",
            "Pagamento à vista",
            "REF-0001");

        using var statement = await _world.Client.StatementAsync(accountId);

        created.Status.ShouldBe(HttpStatusCode.Created, created.Body);

        var item = statement.Json.GetProperty("items").EnumerateArray().Single();

        item.GetRawText().ShouldBe(created.Json.GetRawText());
    }

    [DockerFact]
    public async Task Statement_Items_CarrySixFractionDigitsInBothInstants()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(50.00m);

        using var response = await _world.Client.StatementAsync(accountId);

        var item = StatementItem.ItemsOf(response).Single();

        StatementItem.ParseInstant(item.RecordedAt).ShouldNotBe(default);
        StatementItem.ParseInstant(item.OccurredAt).ShouldNotBe(default);
        response.Status.ShouldBe(HttpStatusCode.OK);
    }
}
