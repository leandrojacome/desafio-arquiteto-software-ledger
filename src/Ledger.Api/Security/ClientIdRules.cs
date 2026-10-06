namespace Ledger.Api.Security;

internal static class ClientIdRules
{
    public const int MaxLength = 128;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < '!' or > '~')
            {
                return false;
            }
        }

        return true;
    }
}
