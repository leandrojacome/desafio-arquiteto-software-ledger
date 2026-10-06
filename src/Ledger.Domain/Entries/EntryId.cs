using System.Text.Json;
using System.Text.Json.Serialization;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Entries;

[JsonConverter(typeof(EntryIdJsonConverter))]
public readonly record struct EntryId
{
    private readonly Guid _value;

    private EntryId(Guid value)
    {
        _value = value;
    }

    public Guid Value => _value == Guid.Empty
        ? throw new InvalidOperationException("The entry id was not created by EntryId.From.")
        : _value;

    public static Result<EntryId> From(Guid value) =>
        value == Guid.Empty ? EntryErrors.NotFound : new EntryId(value);

    public static Result<EntryId> From(string? text) =>
        HyphenatedGuid.TryParse(text, out var value) ? From(value) : EntryErrors.NotFound;

    public override string ToString() => HyphenatedGuid.ToText(_value);
}

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
