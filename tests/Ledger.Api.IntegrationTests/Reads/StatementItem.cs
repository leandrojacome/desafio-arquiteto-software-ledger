using System.Globalization;
using System.Text.Json;

namespace Ledger.Api.IntegrationTests.Reads;

internal sealed record StatementItem(
    string EntryId,
    long AccountVersion,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    string RecordedAt,
    string OccurredAt,
    string? ReversesEntryId,
    string? Description,
    string? Reference)
{
    public decimal SignedAmount => Type == "CREDIT" ? Amount : -Amount;

    public DateTimeOffset RecordedAtInstant => ParseInstant(RecordedAt);

    public static StatementItem From(JsonElement element)
    {
        return new StatementItem(
            element.GetProperty("entryId").GetString() ?? string.Empty,
            element.GetProperty("accountVersion").GetInt64(),
            element.GetProperty("type").GetString() ?? string.Empty,
            ParseMoney(element.GetProperty("amount").GetString()),
            ParseMoney(element.GetProperty("balanceAfter").GetString()),
            element.GetProperty("recordedAt").GetString() ?? string.Empty,
            element.GetProperty("occurredAt").GetString() ?? string.Empty,
            element.GetProperty("reversesEntryId").GetString(),
            element.GetProperty("description").GetString(),
            element.GetProperty("reference").GetString());
    }

    public static IReadOnlyList<StatementItem> ItemsOf(ReadResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return [.. response.Json.GetProperty("items").EnumerateArray().Select(From)];
    }

    public static DateTimeOffset ParseInstant(string text) =>
        DateTimeOffset.ParseExact(
            text,
            "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static string FormatInstant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(string? text) =>
        decimal.Parse(text ?? string.Empty, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}
