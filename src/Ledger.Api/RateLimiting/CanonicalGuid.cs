namespace Ledger.Api.RateLimiting;

internal static class CanonicalGuid
{
    private const int TextLength = 36;

    public static bool TryParse(object? raw, out Guid value)
    {
        value = Guid.Empty;

        if (raw is not string { Length: TextLength } text || !HasCanonicalShape(text))
        {
            return false;
        }

        return Guid.TryParseExact(text, "D", out value) && value != Guid.Empty;
    }

    private static bool HasCanonicalShape(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var isSeparator = index is 8 or 13 or 18 or 23;
            var isExpected = isSeparator ? text[index] == '-' : char.IsAsciiHexDigit(text[index]);

            if (!isExpected)
            {
                return false;
            }
        }

        return true;
    }
}
