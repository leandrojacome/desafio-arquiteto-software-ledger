using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Balances;
using Ledger.Application.Entries;
using Ledger.Application.Outbox;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class SensitiveDtoToStringTests
{
    private static readonly string[] Secrets =
    [
        "80.00",
        "920.00",
        EntryFixtures.Description,
        EntryFixtures.Reference,
        EntryFixtures.KeyText,
        "secret-value",
        "Cobrança em duplicidade confirmada pela conciliação",
        "***.***.789-**"
    ];

    private static void ShouldRevealNothing(object value)
    {
        var text = value.ToString() ?? string.Empty;

        foreach (var secret in Secrets)
        {
            text.ShouldNotContain(secret);
        }
    }

    [Fact]
    public void RegisterEntryCommand_PrintsOnlyIdentifiers()
    {
        var text = EntryFixtures.RegisterCommand().ToString();

        ShouldRevealNothing(EntryFixtures.RegisterCommand());
        text.ShouldContain(EntryFixtures.Account.ToString());
        text.ShouldContain(EntryFixtures.ClientId);
        text.ShouldContain(EntryFixtures.CorrelationId);
    }

    [Fact]
    public void ReverseEntryCommand_PrintsOnlyIdentifiers()
    {
        var command = EntryFixtures.ReverseCommand();

        ShouldRevealNothing(command);
        command.ToString().ShouldContain(EntryFixtures.OriginalEntry.ToString());
    }

    [Fact]
    public void EntryView_PrintsOnlyTheIds()
    {
        var view = EntryFixtures.StoredView();

        ShouldRevealNothing(view);
        view.ToString().ShouldContain(view.Id.ToString());
    }

    [Fact]
    public void EntryOutcomeAndAppliedEntry_PrintOnlyTheIdsOfTheEntry()
    {
        var view = EntryFixtures.StoredView();

        ShouldRevealNothing(new EntryOutcome(view, false));
        ShouldRevealNothing(new AppliedEntry(view, true));
    }

    [Fact]
    public void NewEntry_PrintsOnlyTheIds()
    {
        var entry = Entry.Debit(
            EntryFixtures.Entry,
            EntryFixtures.Account,
            EntryFixtures.Brl(80.00m),
            null,
            EntryFixtures.Description,
            EntryFixtures.Reference).Value;

        ShouldRevealNothing(new NewEntry(entry, EntryFixtures.ClientId, EntryFixtures.CorrelationId));
    }

    [Fact]
    public void IdempotencyRecord_PrintsNeitherTheHashNorTheEntryContent()
    {
        var record = new IdempotencyRecord(new byte[] { 1, 2, 3 }, 1, EntryFixtures.StoredView());

        ShouldRevealNothing(record);
        record.ToString().ShouldNotContain("ReadOnlyMemory");
    }

    [Fact]
    public void OutboxMessage_PrintsNeitherThePayloadNorTheCorrelation()
    {
        var message = new OutboxMessage(
            EntryFixtures.EventGuid,
            EntryFixtures.Account,
            "EntryRegistered",
            "{\"payload\":\"secret-value\",\"balanceAfter\":\"920.00\"}",
            EntryFixtures.CorrelationId,
            null);

        ShouldRevealNothing(message);
    }

    [Fact]
    public void BalanceReadings_PrintNoAmount()
    {
        ShouldRevealNothing(new CurrentBalanceReading("BRL", 80.00m, 0m, EntryFixtures.Entry, DateTimeOffset.UnixEpoch));
        ShouldRevealNothing(new BalanceAtReading("BRL", 0m, 920.00m, EntryFixtures.Entry, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void CreatedAccount_PrintsNoDocumentMaskNorLimit()
    {
        var created = new CreatedAccount(
            EntryFixtures.Account,
            "BRL",
            Money.Create(80.00m, "BRL").Value,
            "***.***.789-**",
            DateTimeOffset.UnixEpoch);

        ShouldRevealNothing(created);
        created.ToString().ShouldContain(EntryFixtures.Account.ToString());
    }

    [Fact]
    public void OutboxEnvelope_PrintsNoPayload()
    {
        var envelope = new OutboxEnvelope(
            EntryFixtures.EventGuid,
            EntryFixtures.Account,
            "EntryRegistered",
            "{\"payload\":\"secret-value\"}",
            EntryFixtures.CorrelationId,
            null,
            DateTimeOffset.UnixEpoch,
            1);

        ShouldRevealNothing(envelope);
    }
}
