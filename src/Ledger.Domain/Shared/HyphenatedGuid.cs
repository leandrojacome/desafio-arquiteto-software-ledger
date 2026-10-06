namespace Ledger.Domain.Shared;

internal static class HyphenatedGuid
{
    private const string Format = "D";
    private const int TextLength = 36;

    public static bool TryParse(string? text, out Guid value)
    {
        if (text is { Length: TextLength } && HasCanonicalShape(text) && Guid.TryParseExact(text, Format, out value))
        {
            return true;
        }

        value = Guid.Empty;

        return false;
    }

    public static string ToText(Guid value) => value.ToString(Format);

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
