using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Contract;

internal static class OpenApiDocument
{
    public const string Route = "/openapi/v1.json";

    private static readonly JsonSerializerOptions Canonical = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string VersionedPath { get; } = System.IO.Path.Combine(
        Observability.RepositoryPaths.Root,
        "docs",
        "05-contratos",
        "openapi.v1.json");

    public static async Task<JsonNode> GenerateAsync()
    {
        using var factory = new LedgerApiFactory();
        using var client = factory.CreateClient();

        var text = await client.GetStringAsync(Route, CancellationToken.None);

        return JsonNode.Parse(text) ?? throw new InvalidOperationException("The OpenAPI document is empty.");
    }

    public static string Canonicalize(JsonNode document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.ToJsonString(Canonical) + "\n";
    }

    public static string FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');

        for (var index = 0; index < Math.Max(expectedLines.Length, actualLines.Length); index++)
        {
            var left = index < expectedLines.Length ? expectedLines[index] : "<end of file>";
            var right = index < actualLines.Length ? actualLines[index] : "<end of file>";

            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                return $"line {index + 1}: versioned has [{left.Trim()}] and the generated document has [{right.Trim()}]";
            }
        }

        return "no difference";
    }
}
