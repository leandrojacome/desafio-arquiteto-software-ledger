using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Ledger.Application.Integrity;

namespace Ledger.Infrastructure.Persistence.Integrity;

internal static class IntegrityAuditDetails
{
    public const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    public static string ForRun(IntegrityRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("mode", summary.Mode.AuditText());
            writer.WriteString("windowStart", Instant(summary.WindowStart));
            writer.WriteString("windowEnd", Instant(summary.WindowEnd));
            writer.WriteNumber("accountsChecked", summary.AccountsChecked);
            writer.WriteNumber("entriesChecked", summary.EntriesChecked);
            writer.WriteNumber("violations", summary.Violations);

            if (summary.Partial)
            {
                writer.WriteBoolean("partial", true);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static string ForViolation(string runId, IntegrityMode mode, IntegrityFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("runId", runId);
            writer.WriteString("mode", mode.AuditText());
            writer.WriteString("check", finding.Check.AuditName());

            if (finding.EntryId is { } entryId)
            {
                writer.WriteString("entryId", entryId.ToString());
            }

            if (finding.AccountVersion is { } accountVersion)
            {
                writer.WriteNumber("accountVersion", accountVersion);
            }

            writer.WriteString("expected", finding.Expected);
            writer.WriteString("found", finding.Found);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static IntegrityRunRecord ParseRun(DateTimeOffset recordedAt, string outcome, string details)
    {
        using var document = JsonDocument.Parse(details);

        var root = document.RootElement;

        var mode = ParseMode(ReadText(root, "mode"));
        var windowStart = ParseInstant(ReadText(root, "windowStart"));
        var windowEnd = ParseInstant(ReadText(root, "windowEnd"));

        return new IntegrityRunRecord(recordedAt, outcome, mode, windowStart, windowEnd);
    }

    private static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture);

    private static string ReadText(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw Unreadable(name)
            : throw Unreadable(name);
    }

    private static IntegrityMode ParseMode(string text)
    {
        return text switch
        {
            "RECENT" => IntegrityMode.Recent,
            "FULL" => IntegrityMode.Full,
            _ => throw Unreadable("mode")
        };
    }

    private static DateTimeOffset ParseInstant(string text)
    {
        return DateTimeOffset.TryParseExact(
            text,
            InstantFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var instant)
            ? instant
            : throw Unreadable("window");
    }

    private static InvalidOperationException Unreadable(string field) =>
        new($"The last integrity run record has an unreadable '{field}' field.");
}
