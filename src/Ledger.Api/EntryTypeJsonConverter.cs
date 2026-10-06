using System.Text.Json;
using System.Text.Json.Serialization;
using Ledger.Domain.Entries;

namespace Ledger.Api;

internal sealed class EntryTypeJsonConverter : JsonConverter<EntryType>
{
    public override EntryType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && EntryTypeText.TryParse(reader.GetString(), out var type))
        {
            return type;
        }

        throw new JsonException("The entry type must be the text CREDIT or DEBIT.");
    }

    public override void Write(Utf8JsonWriter writer, EntryType value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
        {
            throw new JsonException("The entry type is not a defined value.");
        }

        writer.WriteStringValue(value.ToDatabaseText());
    }
}
