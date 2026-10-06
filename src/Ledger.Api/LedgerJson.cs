using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Ledger.Domain.Entries;

namespace Ledger.Api;

internal static class JsonServiceCollectionExtensions
{
    private static readonly JavaScriptEncoder ReadableLatinEncoder = JavaScriptEncoder.Create(new TextEncoderSettings(
        UnicodeRanges.BasicLatin,
        UnicodeRanges.Latin1Supplement,
        UnicodeRanges.LatinExtendedA,
        UnicodeRanges.GeneralPunctuation,
        UnicodeRanges.CurrencySymbols));

    public static IServiceCollection AddLedgerJson(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            options.SerializerOptions.Encoder = ReadableLatinEncoder;
            options.SerializerOptions.Converters.Add(new EntryTypeJsonConverter());
        });

        return services;
    }
}

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
