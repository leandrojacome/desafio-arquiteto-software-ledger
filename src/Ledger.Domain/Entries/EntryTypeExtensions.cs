using Ledger.Domain.Shared;

namespace Ledger.Domain.Entries;

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
