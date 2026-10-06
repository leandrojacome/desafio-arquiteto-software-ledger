using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ledger.Api.Validation;

internal static partial class BodyFields
{
    public static string? Text(JsonObjectBody body, string field, bool required, List<ValidationIssue> issues)
    {
        if (!body.TryGet(field, out var element))
        {
            if (required)
            {
                issues.Add(FieldIssues.Required(field));
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            issues.Add(FieldIssues.InvalidFormat(field));

            return null;
        }

        try
        {
            return element.GetString();
        }
        catch (InvalidOperationException)
        {
            issues.Add(FieldIssues.InvalidEncoding(field));

            return null;
        }
    }

    public static string? Currency(JsonObjectBody body, string field, List<ValidationIssue> issues)
    {
        var text = Text(body, field, true, issues);

        if (text is null)
        {
            return null;
        }

        if (!CurrencyGrammar().IsMatch(text))
        {
            issues.Add(FieldIssues.InvalidFormat(field));

            return null;
        }

        return text;
    }

    public static decimal? Amount(
        JsonObjectBody body,
        string field,
        bool required,
        bool allowZero,
        List<ValidationIssue> issues)
    {
        var text = Text(body, field, required, issues);

        if (text is null)
        {
            return null;
        }

        var reason = MoneyText.TryParse(text, allowZero, out var amount);

        if (reason is null)
        {
            return amount;
        }

        issues.Add(reason switch
        {
            ValidationReasons.TooManyDecimals => FieldIssues.TooManyDecimals(field),
            ValidationReasons.OutOfRange => FieldIssues.OutOfRange(field),
            _ => FieldIssues.InvalidAmount(field)
        });

        return null;
    }

    public static string? Description(JsonObjectBody body, string field, List<ValidationIssue> issues)
    {
        var trimmed = Text(body, field, false, issues)?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var characters = 0;

        foreach (var rune in trimmed.EnumerateRunes())
        {
            if (Rune.IsControl(rune))
            {
                issues.Add(FieldIssues.ControlCharacters(field));

                return null;
            }

            characters++;
        }

        if (characters > EntryRequestLimits.MaxDescriptionLength)
        {
            issues.Add(FieldIssues.TooLong(field, EntryRequestLimits.MaxDescriptionLength));

            return null;
        }

        return trimmed;
    }

    public static string? Reference(JsonObjectBody body, string field, List<ValidationIssue> issues)
    {
        var text = Text(body, field, false, issues);

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (!VisibleAsciiGrammar().IsMatch(text))
        {
            issues.Add(FieldIssues.InvalidReference(field));

            return null;
        }

        if (text.Length > EntryRequestLimits.MaxReferenceLength)
        {
            issues.Add(FieldIssues.TooLong(field, EntryRequestLimits.MaxReferenceLength));

            return null;
        }

        return text;
    }

    public static DateTimeOffset? Instant(
        JsonObjectBody body,
        string field,
        TimeProvider time,
        int futureToleranceMinutes,
        List<ValidationIssue> issues)
    {
        var text = Text(body, field, false, issues);

        if (text is null)
        {
            return null;
        }

        var failure = InstantParameterReader.Read(text, out var instant);

        if (failure != InstantFailure.None)
        {
            issues.Add(InstantIssues.For(field, failure));

            return null;
        }

        if (instant < EntryRequestLimits.EarliestOccurredAt)
        {
            issues.Add(FieldIssues.OutOfRange(field));

            return null;
        }

        if (instant > time.GetUtcNow().AddMinutes(futureToleranceMinutes))
        {
            issues.Add(FieldIssues.InTheFuture(field, futureToleranceMinutes));

            return null;
        }

        return instant;
    }

    [GeneratedRegex("^[A-Z]{3}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyGrammar();

    [GeneratedRegex("^[!-~]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VisibleAsciiGrammar();
}
