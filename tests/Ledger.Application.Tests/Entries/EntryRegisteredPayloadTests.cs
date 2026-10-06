using System.Text;
using System.Text.Json;
using Ledger.Application.Entries;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Domain.Entries;

namespace Ledger.Application.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class EntryRegisteredPayloadTests
{
    private const string ExampleText =
        "{\"eventId\":\"0192b7c4-5d12-7c88-a0e4-9d3b6f1a2c75\",\"eventType\":\"EntryRegistered\",\"schemaVersion\":1," +
        "\"accountId\":\"0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33\",\"entryId\":\"0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10\"," +
        "\"accountVersion\":1843,\"type\":\"DEBIT\",\"amount\":\"80.00\",\"currency\":\"BRL\",\"balanceAfter\":\"920.00\"," +
        "\"recordedAt\":\"2026-10-01T14:03:11.482913Z\",\"occurredAt\":\"2026-10-01T14:03:10.000000Z\"," +
        "\"reversesEntryId\":null,\"correlationId\":\"9f3c1a7e2b4d4f60a1c8e5d7b3a29f10\"}";

    private static readonly string[] ExpectedOrder =
    [
        "eventId", "eventType", "schemaVersion", "accountId", "entryId", "accountVersion", "type", "amount",
        "currency", "balanceAfter", "recordedAt", "occurredAt", "reversesEntryId", "correlationId"
    ];

    private static EntryView View(
        EntryType type = EntryType.Debit,
        decimal amount = 80.00m,
        decimal balanceAfter = 920.00m,
        EntryId? reverses = null,
        DateTimeOffset? recordedAt = null) =>
        EntryFixtures.StoredView(type, amount) with
        {
            BalanceAfter = EntryFixtures.Brl(balanceAfter),
            ReversesEntryId = reverses,
            RecordedAt = recordedAt ?? EntryFixtures.RecordedAt
        };

    [Fact]
    public void Serialize_KnownEntry_ProducesTheExampleOfTheContractByteForByte()
    {
        var payload = EntryRegisteredPayload.Serialize(View(), EntryFixtures.EventGuid, EntryFixtures.CorrelationId);

        payload.ShouldBe(ExampleText);
    }

    [Fact]
    public void Serialize_KnownEntry_WritesThePropertiesInTheContractOrder()
    {
        var payload = EntryRegisteredPayload.Serialize(View(), EntryFixtures.EventGuid, EntryFixtures.CorrelationId);

        using var document = JsonDocument.Parse(payload);

        document.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(ExpectedOrder);
    }

    [Fact]
    public void Serialize_PlainEntry_WritesReversesEntryIdAsExplicitNull()
    {
        var payload = EntryRegisteredPayload.Serialize(View(), EntryFixtures.EventGuid, EntryFixtures.CorrelationId);

        using var document = JsonDocument.Parse(payload);

        document.RootElement.GetProperty("reversesEntryId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void Serialize_Reversal_WritesTheReversedEntryId()
    {
        var view = View(EntryType.Credit, 150.00m, 1000.00m, EntryFixtures.OriginalEntry);

        var payload = EntryRegisteredPayload.Serialize(view, EntryFixtures.EventGuid, EntryFixtures.CorrelationId);

        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("reversesEntryId").GetString()
            .ShouldBe("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f11");
        document.RootElement.GetProperty("type").GetString().ShouldBe("CREDIT");
    }

    [Fact]
    public void Serialize_NegativeBalance_WritesTheSignedTextWithTwoDecimals()
    {
        var payload = EntryRegisteredPayload.Serialize(
            View(balanceAfter: -20m),
            EntryFixtures.EventGuid,
            EntryFixtures.CorrelationId);

        using var document = JsonDocument.Parse(payload);

        document.RootElement.GetProperty("balanceAfter").GetString().ShouldBe("-20.00");
    }

    [Fact]
    public void Serialize_Instants_AlwaysCarrySixFractionalDigits()
    {
        var view = View(recordedAt: new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero));

        var payload = EntryRegisteredPayload.Serialize(view, EntryFixtures.EventGuid, EntryFixtures.CorrelationId);

        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("recordedAt").GetString().ShouldBe("2026-10-01T14:03:11.000000Z");
        document.RootElement.GetProperty("occurredAt").GetString().ShouldBe("2026-10-01T14:03:10.000000Z");
    }

    [Fact]
    public void Serialize_InstantsWithOffset_AreWrittenInUtc()
    {
        var view = View() with
        {
            RecordedAt = new DateTimeOffset(2026, 10, 1, 11, 3, 11, TimeSpan.FromHours(-3)).AddTicks(4_829_130)
        };

        var payload = EntryRegisteredPayload.Serialize(view, EntryFixtures.EventGuid, EntryFixtures.CorrelationId);

        using var document = JsonDocument.Parse(payload);

        document.RootElement.GetProperty("recordedAt").GetString().ShouldBe("2026-10-01T14:03:11.482913Z");
    }

    [Fact]
    public void Serialize_EntryWithFreeText_NeverWritesDescriptionReferenceOrClient()
    {
        var payload = EntryRegisteredPayload.Serialize(View(), EntryFixtures.EventGuid, EntryFixtures.CorrelationId);

        using var document = JsonDocument.Parse(payload);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();

        names.ShouldNotContain("description");
        names.ShouldNotContain("reference");
        names.ShouldNotContain("clientId");
        payload.ShouldNotContain(EntryFixtures.Description);
        payload.ShouldNotContain(EntryFixtures.Reference);
        payload.ShouldNotContain(EntryFixtures.ClientId);
    }

    [Fact]
    public void Serialize_Always_ProducesTextWithoutByteOrderMark()
    {
        var payload = EntryRegisteredPayload.Serialize(View(), EntryFixtures.EventGuid, EntryFixtures.CorrelationId);

        payload[0].ShouldBe('{');
        Encoding.UTF8.GetBytes(payload).Take(3).ShouldNotBe(Encoding.UTF8.Preamble.ToArray());
    }

    [Fact]
    public void Serialize_CorrelationIdWithSymbols_KeepsItReadable()
    {
        var payload = EntryRegisteredPayload.Serialize(View(), EntryFixtures.EventGuid, "abc+12/34=");

        using var document = JsonDocument.Parse(payload);

        document.RootElement.GetProperty("correlationId").GetString().ShouldBe("abc+12/34=");
        payload.ShouldContain("abc+12/34=");
    }
}
