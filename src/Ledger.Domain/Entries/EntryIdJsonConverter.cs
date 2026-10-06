using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ledger.Domain.Entries;

internal sealed class EntryIdJsonConverter : JsonConverter<EntryId>
{
    public override EntryId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("The entry id must be a JSON string.");
        }

        var result = EntryId.From(reader.GetString());

        return result.IsSuccess
            ? result.Value
            : throw new JsonException("The entry id must be a UUID in the hyphenated format.");
    }

    public override void Write(Utf8JsonWriter writer, EntryId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
