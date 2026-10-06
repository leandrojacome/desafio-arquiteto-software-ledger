using System.Diagnostics.CodeAnalysis;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Entries;

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let an unset entry type pass for a valid one; the default must stay outside the defined values.")]
public enum EntryType
{
    Credit = 1,
    Debit = 2
}

public static class EntryTypeExtensions
{
    public static EntryType Opposite(this EntryType type)
    {
        return type switch
        {
            EntryType.Credit => EntryType.Debit,
            EntryType.Debit => EntryType.Credit,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown entry type.")
        };
    }

    public static string ToDatabaseText(this EntryType type)
    {
        return type switch
        {
            EntryType.Credit => EntryTypeText.Credit,
            EntryType.Debit => EntryTypeText.Debit,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown entry type.")
        };
    }

    public static decimal SignedDelta(this EntryType type, Money amount)
    {
        return type switch
        {
            EntryType.Credit => amount.Amount,
            EntryType.Debit => -amount.Amount,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown entry type.")
        };
    }
}

public static class EntryTypeText
{
    internal const string Credit = "CREDIT";
    internal const string Debit = "DEBIT";

    public static bool TryParse(string? text, out EntryType type)
    {
        switch (text)
        {
            case Credit:
                type = EntryType.Credit;
                return true;
            case Debit:
                type = EntryType.Debit;
                return true;
            default:
                type = default;
                return false;
        }
    }
}
