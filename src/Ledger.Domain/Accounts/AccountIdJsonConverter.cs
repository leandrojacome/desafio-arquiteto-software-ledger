using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ledger.Domain.Accounts;

internal sealed class AccountIdJsonConverter : JsonConverter<AccountId>
{
    public override AccountId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("The account id must be a JSON string.");
        }

        var result = AccountId.From(reader.GetString());

        return result.IsSuccess
            ? result.Value
            : throw new JsonException("The account id must be a UUID in the hyphenated format.");
    }

    public override void Write(Utf8JsonWriter writer, AccountId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
