using System.Globalization;
using System.Text.RegularExpressions;

namespace Ledger.Api.Validation;

internal static partial class InstantParameterReader
{
    private const string LocalGrammar = "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]{1,6})?";
    private const string NegativeZeroOffset = "-00:00";
    private const string FractionSeparator = ".";

    private static readonly string[] OffsetFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFK"
    ];

    private static readonly string[] LocalFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFF"
    ];

    public static bool TryRead(string? text, out DateTimeOffset instant) =>
        Read(text, out instant) == InstantFailure.None;

    public static InstantFailure Read(string? text, out DateTimeOffset instant)
    {
        instant = default;

        if (text is null || !TryDropZeroPadding(text, out var normalized))
        {
            return InstantFailure.Malformed;
        }

        if (LocalGrammarOnly().IsMatch(normalized))
        {
            return IsValidLocalInstant(normalized) ? InstantFailure.MissingTimeZone : InstantFailure.Malformed;
        }

        if (!OffsetGrammar().IsMatch(normalized) || normalized.EndsWith(NegativeZeroOffset, StringComparison.Ordinal))
        {
            return InstantFailure.Malformed;
        }

        return DateTimeOffset.TryParseExact(
            normalized,
            OffsetFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal,
            out instant)
            ? InstantFailure.None
            : InstantFailure.Malformed;
    }

    private static bool TryDropZeroPadding(string text, out string trimmed)
    {
        trimmed = text;

        var separator = text.IndexOf(FractionSeparator, StringComparison.Ordinal);

        if (separator < 0)
        {
            return true;
        }

        var start = separator + 1;
        var end = start;

        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        var digits = end - start;

        if (digits <= EntryRequestLimits.MaxFractionDigitsOfInstant)
        {
            return true;
        }

        if (digits > EntryRequestLimits.MaxZeroPaddedFractionDigitsOfInstant)
        {
            return false;
        }

        var keptEnd = start + EntryRequestLimits.MaxFractionDigitsOfInstant;

        if (text.AsSpan(keptEnd, end - keptEnd).IndexOfAnyExcept('0') >= 0)
        {
            return false;
        }

        trimmed = text.Remove(keptEnd, end - keptEnd);

        return true;
    }

    private static bool IsValidLocalInstant(string text) =>
        DateTime.TryParseExact(text, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    [GeneratedRegex(LocalGrammar + "\\z", RegexOptions.CultureInvariant)]
    private static partial Regex LocalGrammarOnly();

    [GeneratedRegex(LocalGrammar + "(Z|[+-][0-9]{2}:[0-9]{2})\\z", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetGrammar();
}
