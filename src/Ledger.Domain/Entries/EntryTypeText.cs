namespace Ledger.Domain.Entries;

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
