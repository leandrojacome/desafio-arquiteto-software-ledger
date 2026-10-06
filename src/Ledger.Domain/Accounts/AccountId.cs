using System.Text.Json;
using System.Text.Json.Serialization;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Accounts;

[JsonConverter(typeof(AccountIdJsonConverter))]
public readonly record struct AccountId
{
    private readonly Guid _value;

    private AccountId(Guid value)
    {
        _value = value;
    }

    public Guid Value => _value == Guid.Empty
        ? throw new InvalidOperationException("The account id was not created by AccountId.From.")
        : _value;

    public static Result<AccountId> From(Guid value) =>
        value == Guid.Empty ? AccountErrors.NotFound : new AccountId(value);

    public static Result<AccountId> From(string? text) =>
        HyphenatedGuid.TryParse(text, out var value) ? From(value) : AccountErrors.NotFound;

    public override string ToString() => HyphenatedGuid.ToText(_value);
}

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
