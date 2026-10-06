using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Tests.Entries.Support;

internal static class EntryFixtures
{
    public const string ClientId = "pix-core";
    public const string CorrelationId = "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10";
    public const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    public const string Description = "Pix enviado";
    public const string Reference = "E18236120202610011403s0a1b2c3d4e";
    public const string KeyText = "qs-debit-0001";
    public const string KeyFingerprint = "889585dc";

    public static readonly Guid EntryGuid = Guid.Parse("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10");
    public static readonly Guid EventGuid = Guid.Parse("0192b7c4-5d12-7c88-a0e4-9d3b6f1a2c75");
    public static readonly Guid OriginalGuid = Guid.Parse("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f11");

    public static readonly AccountId Account = AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;
    public static readonly EntryId Entry = EntryId.From(EntryGuid).Value;
    public static readonly EntryId OriginalEntry = EntryId.From(OriginalGuid).Value;
    public static readonly IdempotencyKey Key = IdempotencyKey.From(KeyText).Value;

    public static readonly DateTimeOffset OccurredAt = new(2026, 10, 1, 14, 3, 10, TimeSpan.Zero);

    public static readonly DateTimeOffset RecordedAt =
        new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero).AddTicks(4_829_130);

    public static Money Brl(decimal amount) => Money.Create(amount, "BRL").Value;

    public static RegisterEntryCommand RegisterCommand(
        EntryType type = EntryType.Debit,
        decimal amount = 80.00m,
        IdempotencyKey? key = null,
        string? description = Description,
        string? reference = Reference,
        DateTimeOffset? occurredAt = null) =>
        new(
            Account,
            key ?? Key,
            type,
            Brl(amount),
            occurredAt ?? OccurredAt,
            description,
            reference,
            ClientId,
            CorrelationId,
            TraceParent);

    public static ReverseEntryCommand ReverseCommand(
        IdempotencyKey? key = null,
        string? description = "Cobrança em duplicidade confirmada pela conciliação") =>
        new(Account, OriginalEntry, key ?? Key, description, ClientId, CorrelationId, TraceParent);

    public static EntryView ViewOf(
        NewEntry entry,
        long accountVersion = 1843,
        decimal balanceAfter = 920.00m)
    {
        var written = entry.Entry;

        return new EntryView(
            written.Id,
            written.AccountId,
            accountVersion,
            written.Type,
            written.Amount,
            Brl(balanceAfter),
            RecordedAt,
            written.OccurredAt ?? RecordedAt,
            written.Description,
            written.Reference,
            written.ReversesEntryId);
    }

    public static EntryView StoredView(EntryType type = EntryType.Debit, decimal amount = 80.00m) =>
        new(
            Entry,
            Account,
            1843,
            type,
            Brl(amount),
            Brl(920.00m),
            RecordedAt,
            OccurredAt,
            Description,
            Reference,
            null);

    public static ReversalCandidate Candidate(
        EntryType type = EntryType.Debit,
        decimal amount = 150.00m,
        EntryId? reversesEntryId = null,
        EntryId? reversalId = null) =>
        new(OriginalEntry, type, Brl(amount), reversesEntryId, reversalId);

    public static AccountBalance AccountSnapshot(decimal balance, decimal overdraftLimit = 0m, string currency = "BRL") =>
        AccountBalance.Create(Account, currency, balance, overdraftLimit).Value;
}
