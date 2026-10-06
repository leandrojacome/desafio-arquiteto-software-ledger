using Microsoft.Net.Http.Headers;

namespace Ledger.Api.Validation;

internal static class JsonContentType
{
    private const string MediaType = "application/json";
    private const string Utf8 = "utf-8";

    public static bool IsAccepted(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed))
        {
            return false;
        }

        if (!parsed.MediaType.Equals(MediaType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !parsed.Charset.HasValue || parsed.Charset.Equals(Utf8, StringComparison.OrdinalIgnoreCase);
    }
}
