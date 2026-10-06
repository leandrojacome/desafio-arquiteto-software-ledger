using System.Net;
using System.Text.RegularExpressions;

namespace Ledger.Api.IntegrationTests.Reads;

internal static partial class ReadProblemAssertions
{
    private const string TypeBase = "https://ledger.bank.internal/problems/";

    private static readonly string[] InternalMarkers =
    [
        "Exception", "   at ", "Npgsql", "SELECT ", "INSERT ", "127.0.0.1", "Host=", "Password", "Stack"
    ];

    public static void ShouldBeProblem(
        this ReadResponse response,
        HttpStatusCode status,
        string code,
        string title,
        string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Status.ShouldBe(status, response.Body);
        response.ContentType.ShouldBe("application/problem+json");

        var root = response.Json;

        root.GetProperty("code").GetString().ShouldBe(code);
        root.GetProperty("title").GetString().ShouldBe(title);
        root.GetProperty("status").GetInt32().ShouldBe((int)status);
        root.GetProperty("type").GetString().ShouldBe(TypeBase + Slug(code));

        if (instance is not null)
        {
            root.GetProperty("instance").GetString().ShouldBe(instance);
        }

        var correlationId = response.Header("X-Correlation-Id");

        correlationId.ShouldNotBeNullOrWhiteSpace();
        root.GetProperty("correlationId").GetString().ShouldBe(correlationId);
        TraceId().IsMatch(root.GetProperty("traceId").GetString() ?? string.Empty).ShouldBeTrue(response.Body);
    }

    public static void ShouldCarryNoInternals(this ReadResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        foreach (var marker in InternalMarkers)
        {
            response.Body.ShouldNotContain(marker, Case.Sensitive);
        }
    }

    public static IReadOnlyList<(string Field, string Reason, string Message)> Errors(this ReadResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return
        [
            .. response.Json.GetProperty("errors").EnumerateArray().Select(item => (
                item.GetProperty("field").GetString() ?? string.Empty,
                item.GetProperty("reason").GetString() ?? string.Empty,
                item.GetProperty("message").GetString() ?? string.Empty))
        ];
    }

    private static string Slug(string code) =>
        new([.. code.Select(character => character == '_' ? '-' : char.ToLowerInvariant(character))]);

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex TraceId();
}
