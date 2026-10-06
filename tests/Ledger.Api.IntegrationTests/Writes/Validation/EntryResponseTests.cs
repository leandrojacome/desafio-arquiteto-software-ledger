using System.Text.Json;
using Ledger.Api.Contracts;
using Ledger.Application.Accounts;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Writes.Validation;

[Trait("Category", "Unit")]
public sealed class EntryResponseTests
{
    private static readonly Guid AccountGuid = Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");
    private static readonly Guid EntryGuid = Guid.Parse("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10");
    private static readonly Guid OriginalGuid = Guid.Parse("0192b7c9-3e40-7b5d-8a17-c04d2f6e9b51");

    private static JsonSerializerOptions HttpOptions()
    {
        using var provider = new ServiceCollection().AddLedgerJson().BuildServiceProvider();

        return provider.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
    }

    private static EntryView View(
        EntryType type = EntryType.Debit,
        string amount = "80.00",
        string balanceAfter = "920.00",
        string? description = "Pix enviado",
        string? reference = "E18236120202610011403s0a1b2c3d4e",
        Guid? reverses = null) =>
        new(
            EntryId.From(EntryGuid).Value,
            AccountId.From(AccountGuid).Value,
            1843,
            type,
            Money.Create(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "BRL").Value,
            Money.Create(decimal.Parse(balanceAfter, System.Globalization.CultureInfo.InvariantCulture), "BRL").Value,
            new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero).AddTicks(4829130),
            new DateTimeOffset(2026, 10, 1, 14, 3, 10, TimeSpan.Zero),
            description,
            reference,
            reverses is { } id ? EntryId.From(id).Value : null);

    private static JsonElement Serialize(EntryView view)
    {
        var json = JsonSerializer.Serialize(EntryResponse.From(view), HttpOptions());

        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    [Fact]
    public void Response_HasTheTwelvePropertiesOfTheContractInCamelCaseAndOrder()
    {
        var root = Serialize(View());

        root.EnumerateObject().Select(property => property.Name).ShouldBe(
        [
            "entryId", "accountId", "accountVersion", "type", "amount", "currency", "balanceAfter",
            "occurredAt", "recordedAt", "reversesEntryId", "description", "reference"
        ]);
    }

    [Fact]
    public void Response_FormatsIdentifiersMoneyAndInstants()
    {
        var root = Serialize(View());

        root.GetProperty("entryId").GetString().ShouldBe("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10");
        root.GetProperty("accountId").GetString().ShouldBe("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");
        root.GetProperty("accountVersion").GetInt64().ShouldBe(1843);
        root.GetProperty("type").GetString().ShouldBe("DEBIT");
        root.GetProperty("amount").GetString().ShouldBe("80.00");
        root.GetProperty("currency").GetString().ShouldBe("BRL");
        root.GetProperty("balanceAfter").GetString().ShouldBe("920.00");
        root.GetProperty("occurredAt").GetString().ShouldBe("2026-10-01T14:03:10.000000Z");
        root.GetProperty("recordedAt").GetString().ShouldBe("2026-10-01T14:03:11.482913Z");
        root.GetProperty("description").GetString().ShouldBe("Pix enviado");
        root.GetProperty("reference").GetString().ShouldBe("E18236120202610011403s0a1b2c3d4e");
    }

    [Fact]
    public void Response_WritesNullsExplicitlyForTheOptionalProperties()
    {
        var root = Serialize(View(description: null, reference: null));

        root.GetProperty("reversesEntryId").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("description").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("reference").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void Response_OfAReversal_CarriesTheOriginalIdentifier()
    {
        var root = Serialize(View(EntryType.Credit, reverses: OriginalGuid));

        root.GetProperty("type").GetString().ShouldBe("CREDIT");
        root.GetProperty("reversesEntryId").GetString().ShouldBe("0192b7c9-3e40-7b5d-8a17-c04d2f6e9b51");
    }

    [Theory]
    [InlineData("-50.00", "-50.00")]
    [InlineData("0.5", "0.50")]
    [InlineData("1000", "1000.00")]
    public void Response_WritesMoneyWithTwoDecimalPlaces(string balance, string expected)
    {
        Serialize(View(balanceAfter: balance)).GetProperty("balanceAfter").GetString().ShouldBe(expected);
    }

    [Fact]
    public void Response_ConvertsInstantsWithAnOffsetToUtc()
    {
        var view = View() with { OccurredAt = new DateTimeOffset(2026, 10, 1, 11, 3, 10, TimeSpan.FromHours(-3)) };

        Serialize(view).GetProperty("occurredAt").GetString().ShouldBe("2026-10-01T14:03:10.000000Z");
    }

    [Fact]
    public void AccountResponse_FormatsTheCreatedAccountWithoutTheDocument()
    {
        var document = HolderDocument.From("12345678909").Value;
        var created = new CreatedAccount(
            AccountId.From(AccountGuid).Value,
            "BRL",
            Money.Create(50.5m, "BRL").Value,
            document.Masked(),
            new DateTimeOffset(2026, 9, 14, 12, 0, 3, TimeSpan.Zero).AddTicks(4152060));

        var json = JsonSerializer.Serialize(CreateAccountResponse.From(created), HttpOptions());

        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        root.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["accountId", "currency", "overdraftLimit", "holderDocumentMasked", "createdAt"]);
        root.GetProperty("overdraftLimit").GetString().ShouldBe("50.50");
        root.GetProperty("holderDocumentMasked").GetString().ShouldBe("***.***.789-**");
        root.GetProperty("createdAt").GetString().ShouldBe("2026-09-14T12:00:03.415206Z");
        json.ShouldNotContain("12345678909");
    }
}
