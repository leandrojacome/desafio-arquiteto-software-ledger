using System.Globalization;
using System.Text.RegularExpressions;

namespace Ledger.Api.Validation;

internal static partial class MoneyText
{
    private const char DecimalSeparator = '.';
    private const char MinusSign = '-';

    public static string? TryParse(string text, bool allowZero, out decimal amount)
    {
        amount = decimal.Zero;

        if (!Grammar().IsMatch(text))
        {
            return ValidationReasons.InvalidFormat;
        }

        if (text[0] == MinusSign)
        {
            return ValidationReasons.OutOfRange;
        }

        if (FractionDigits(text) > EntryRequestLimits.MaxDecimalPlaces)
        {
            return ValidationReasons.TooManyDecimals;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return ValidationReasons.OutOfRange;
        }

        if ((parsed == decimal.Zero && !allowZero) || parsed > EntryRequestLimits.MaxAmount)
        {
            return ValidationReasons.OutOfRange;
        }

        amount = parsed;

        return null;
    }

    private static int FractionDigits(string text)
    {
        var separator = text.IndexOf(DecimalSeparator, StringComparison.Ordinal);

        return separator < 0 ? 0 : text.Length - separator - 1;
    }

    [GeneratedRegex("^-?[0-9]+(\\.[0-9]+)?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Grammar();
}
