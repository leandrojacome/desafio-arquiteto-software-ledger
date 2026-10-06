using System.Globalization;

namespace Ledger.Api.Contracts;

internal static class InstantText
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    public static string From(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);
}
