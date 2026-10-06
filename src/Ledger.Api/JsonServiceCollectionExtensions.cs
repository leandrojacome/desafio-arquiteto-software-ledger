using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

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
