using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ledger.Domain.Entries;

namespace Ledger.Application.Entries;

internal static class EntryRegisteredPayload
{
    public const string EventType = "EntryRegistered";
    public const int SchemaVersion = 1;

    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false
    };

    public static string Serialize(EntryView entry, Guid eventId, string correlationId)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("eventId", eventId.ToString("D"));
            writer.WriteString("eventType", EventType);
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("accountId", entry.AccountId.ToString());
            writer.WriteString("entryId", entry.Id.ToString());
            writer.WriteNumber("accountVersion", entry.AccountVersion);
            writer.WriteString("type", entry.Type.ToDatabaseText());
            writer.WriteString("amount", entry.Amount.ToDecimalString());
            writer.WriteString("currency", entry.Amount.Currency);
            writer.WriteString("balanceAfter", entry.BalanceAfter.ToDecimalString());
            writer.WriteString("recordedAt", FormatInstant(entry.RecordedAt));
            writer.WriteString("occurredAt", FormatInstant(entry.OccurredAt));
            WriteReversesEntryId(writer, entry.ReversesEntryId);
            writer.WriteString("correlationId", correlationId);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteReversesEntryId(Utf8JsonWriter writer, EntryId? reversesEntryId)
    {
        if (reversesEntryId is { } original)
        {
            writer.WriteString("reversesEntryId", original.ToString());
            return;
        }

        writer.WriteNull("reversesEntryId");
    }

    private static string FormatInstant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture);
}
