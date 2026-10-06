using System.Globalization;
using System.Net;
using Ledger.Api.IntegrationTests.Reads;

namespace Ledger.Api.IntegrationTests.TimeZones;

internal static class TimeZoneAssertions
{
    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffffzzz";

    public static string Spell(DateTimeOffset instant, TimeSpan offset) =>
        instant.ToOffset(offset).ToString(InstantFormat, CultureInfo.InvariantCulture);

    public static string SpellInUtc(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    public static void ShouldBeProblemCode(this ReadResponse response, HttpStatusCode status, string code)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Status.ShouldBe(status, response.Body);
        response.ContentType.ShouldBe("application/problem+json");
        response.Text("code").ShouldBe(code);
    }
}
