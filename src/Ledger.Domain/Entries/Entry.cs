using System.Text;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Entries;

public sealed class Entry
{
    public const int MaxDescriptionLength = 140;
    public const int MaxReferenceLength = 100;

    private Entry(
        EntryId id,
        AccountId accountId,
        EntryType type,
        Money amount,
        DateTimeOffset? occurredAt,
        string? description,
        string? reference,
        EntryId? reversesEntryId)
    {
        Id = id;
        AccountId = accountId;
        Type = type;
        Amount = amount;
        OccurredAt = occurredAt;
        Description = description;
        Reference = reference;
        ReversesEntryId = reversesEntryId;
    }

    public EntryId Id { get; }

    public AccountId AccountId { get; }

    public EntryType Type { get; }

    public Money Amount { get; }

    public DateTimeOffset? OccurredAt { get; }

    public string? Description { get; }

    public string? Reference { get; }

    public EntryId? ReversesEntryId { get; }

    public bool IsReversal => ReversesEntryId is not null;

    public decimal SignedDelta => Type.SignedDelta(Amount);

    public static Result<Entry> Credit(
        EntryId id,
        AccountId accountId,
        Money amount,
        DateTimeOffset? occurredAt,
        string? description,
        string? reference) =>
        Create(id, accountId, EntryType.Credit, amount, occurredAt, description, reference, null);

    public static Result<Entry> Debit(
        EntryId id,
        AccountId accountId,
        Money amount,
        DateTimeOffset? occurredAt,
        string? description,
        string? reference) =>
        Create(id, accountId, EntryType.Debit, amount, occurredAt, description, reference, null);

    public static Result<Entry> ReversalOf(
        ReversalPlan plan,
        EntryId id,
        AccountId accountId,
        string? description) =>
        Create(id, accountId, plan.Type, plan.Amount, null, description, null, plan.OriginalId);

    private static Result<Entry> Create(
        EntryId id,
        AccountId accountId,
        EntryType type,
        Money amount,
        DateTimeOffset? occurredAt,
        string? description,
        string? reference,
        EntryId? reversesEntryId)
    {
        if (!amount.IsPositive)
        {
            return MoneyErrors.MustBePositive;
        }

        if (!TryNormalizeDescription(description, out var normalizedDescription))
        {
            return EntryErrors.InvalidDescription;
        }

        if (!TryNormalizeReference(reference, out var normalizedReference))
        {
            return EntryErrors.InvalidReference;
        }

        return new Entry(
            id,
            accountId,
            type,
            amount,
            occurredAt?.ToUniversalTime(),
            normalizedDescription,
            normalizedReference,
            reversesEntryId);
    }

    private static bool TryNormalizeDescription(string? description, out string? normalized)
    {
        normalized = null;
        var trimmed = description?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        if (!IsValidDescription(trimmed))
        {
            return false;
        }

        normalized = trimmed;

        return true;
    }

    private static bool IsValidDescription(string text)
    {
        var characters = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            characters++;

            if (characters > MaxDescriptionLength || Rune.IsControl(rune))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryNormalizeReference(string? reference, out string? normalized)
    {
        normalized = null;

        if (string.IsNullOrEmpty(reference))
        {
            return true;
        }

        if (reference.Length > MaxReferenceLength || !VisibleAscii.ContainsOnlyVisible(reference))
        {
            return false;
        }

        normalized = reference;

        return true;
    }
}
