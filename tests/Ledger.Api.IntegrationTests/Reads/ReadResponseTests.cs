using System.Text.Json;
using Ledger.Api.Contracts;
using Ledger.Application.Balances;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Reads;

[Trait("Category", "Unit")]
public sealed class ReadResponseTests
{
    private static readonly AccountId Account = AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;

    private static readonly EntryId LastEntry = EntryId.From(Guid.Parse("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10")).Value;

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero).AddTicks(4_829_130);

    [Fact]
    public void BalanceResponse_ForTheCurrentBalance_HasTheSixFieldsAndNoSettled()
    {
        var view = new BalanceView(Account, Brl(850m), Brl(0m), LastEntry, Instant, null);

        using var json = Serialize(BalanceResponse.From(view));

        var root = json.RootElement;

        PropertyNames(root).ShouldBe(["accountId", "currency", "balance", "overdraftLimit", "asOf", "lastEntryId"]);
        root.GetProperty("accountId").GetString().ShouldBe("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");
        root.GetProperty("currency").GetString().ShouldBe("BRL");
        root.GetProperty("balance").GetString().ShouldBe("850.00");
        root.GetProperty("overdraftLimit").GetString().ShouldBe("0.00");
        root.GetProperty("asOf").GetString().ShouldBe("2026-10-01T14:03:11.482913Z");
        root.GetProperty("lastEntryId").GetString().ShouldBe("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10");
    }

    [Fact]
    public void BalanceResponse_WithoutALastEntry_WritesAnExplicitNull()
    {
        var view = new BalanceView(Account, Brl(0m), Brl(0m), null, Instant, null);

        using var json = Serialize(BalanceResponse.From(view));

        json.RootElement.GetProperty("lastEntryId").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("balance").GetString().ShouldBe("0.00");
    }

    [Fact]
    public void BalanceResponse_WithANegativeBalance_KeepsTheSignAndTwoDecimals()
    {
        var view = new BalanceView(Account, Brl(-50m), Brl(50m), LastEntry, Instant, null);

        using var json = Serialize(BalanceResponse.From(view));

        json.RootElement.GetProperty("balance").GetString().ShouldBe("-50.00");
        json.RootElement.GetProperty("overdraftLimit").GetString().ShouldBe("50.00");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BalanceResponse_ForAHistoricalBalance_CarriesSettledAsABoolean(bool settled)
    {
        var view = new BalanceView(Account, Brl(920m), Brl(0m), LastEntry, Instant, settled);

        using var json = Serialize(BalanceResponse.From(view));

        PropertyNames(json.RootElement).ShouldBe(
            ["accountId", "currency", "balance", "overdraftLimit", "asOf", "lastEntryId", "settled"]);
        json.RootElement.GetProperty("settled").GetBoolean().ShouldBe(settled);
    }

    [Fact]
    public void BalanceResponse_WithAWholeSecondInstant_KeepsSixFractionDigits()
    {
        var wholeSecond = new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero);
        var view = new BalanceView(Account, Brl(1m), Brl(0m), null, wholeSecond, true);

        using var json = Serialize(BalanceResponse.From(view));

        json.RootElement.GetProperty("asOf").GetString().ShouldBe("2026-10-01T14:03:11.000000Z");
    }

    [Fact]
    public void StatementResponse_HasTheItemsTheCursorAndTheLimit()
    {
        var page = new StatementPage([Entry(EntryType.Debit, 80m, 920m), Entry(EntryType.Credit, 80m, 1000m)], "abc", 2);

        using var json = Serialize(StatementResponse.From(page));

        var root = json.RootElement;

        PropertyNames(root).ShouldBe(["items", "nextCursor", "limit"]);
        root.GetProperty("items").GetArrayLength().ShouldBe(2);
        root.GetProperty("nextCursor").GetString().ShouldBe("abc");
        root.GetProperty("limit").GetInt32().ShouldBe(2);
    }

    [Fact]
    public void StatementResponse_OnTheLastPage_WritesAnExplicitNullCursor()
    {
        using var json = Serialize(StatementResponse.From(new StatementPage([], null, 50)));

        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
        json.RootElement.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void StatementItem_HasTheTwelveFieldsOfTheEntryContract()
    {
        var view = Entry(EntryType.Debit, 80m, 920m, "Pix enviado", "E18236120202610011403s0a1b2c3d4e", null);

        using var json = Serialize(EntryResponse.From(view));

        var root = json.RootElement;

        PropertyNames(root).ShouldBe(
        [
            "entryId", "accountId", "accountVersion", "type", "amount", "currency", "balanceAfter", "occurredAt",
            "recordedAt", "reversesEntryId", "description", "reference"
        ]);
        root.GetProperty("entryId").GetString().ShouldBe(view.Id.ToString());
        root.GetProperty("accountVersion").GetInt64().ShouldBe(1843);
        root.GetProperty("type").GetString().ShouldBe("DEBIT");
        root.GetProperty("amount").GetString().ShouldBe("80.00");
        root.GetProperty("currency").GetString().ShouldBe("BRL");
        root.GetProperty("balanceAfter").GetString().ShouldBe("920.00");
        root.GetProperty("occurredAt").GetString().ShouldBe("2026-10-01T14:03:10.000000Z");
        root.GetProperty("recordedAt").GetString().ShouldBe("2026-10-01T14:03:11.482913Z");
        root.GetProperty("reversesEntryId").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("description").GetString().ShouldBe("Pix enviado");
        root.GetProperty("reference").GetString().ShouldBe("E18236120202610011403s0a1b2c3d4e");
    }

    [Fact]
    public void StatementItem_OfAReversalWithoutTextFields_WritesExplicitNulls()
    {
        var view = Entry(EntryType.Credit, 80m, 1000m, null, null, LastEntry);

        using var json = Serialize(EntryResponse.From(view));

        json.RootElement.GetProperty("type").GetString().ShouldBe("CREDIT");
        json.RootElement.GetProperty("reversesEntryId").GetString().ShouldBe(LastEntry.ToString());
        json.RootElement.GetProperty("description").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("reference").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static Money Brl(decimal amount) => Money.Create(amount, "BRL").Value;

    private static EntryView Entry(
        EntryType type,
        decimal amount,
        decimal balanceAfter,
        string? description = null,
        string? reference = null,
        EntryId? reverses = null) =>
        new(
            EntryId.From(Guid.CreateVersion7()).Value,
            Account,
            1843,
            type,
            Brl(amount),
            Brl(balanceAfter),
            Instant,
            Instant.AddSeconds(-1).AddTicks(-4_829_130),
            description,
            reference,
            reverses);

    private static List<string> PropertyNames(JsonElement element) =>
        [.. element.EnumerateObject().Select(property => property.Name)];

    private static JsonDocument Serialize<TBody>(TBody body)
    {
        var services = new ServiceCollection();

        services.AddLedgerJson();

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        return JsonDocument.Parse(JsonSerializer.Serialize(body, options));
    }
}
