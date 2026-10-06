using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ledger.Api.IntegrationTests.Writes.Support;

internal static partial class ProblemAssertions
{
    private const string TypeBase = "https://ledger.bank.internal/problems/";

    private static readonly string[] DescriptiveProperties = ["title", "detail"];

    public static JsonElement ShouldBeProblem(this ApiResponse response, int status, string code)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.StatusCode.ShouldBe(status, response.Body);
        response.ContentType.ShouldBe("application/problem+json");

        var problem = response.Json();

        problem.GetProperty("status").GetInt32().ShouldBe(status);
        problem.GetProperty("code").GetString().ShouldBe(code);
        var type = problem.GetProperty("type").GetString().ShouldNotBeNull();

        string.Equals(type, TypeBase + code.Replace('_', '-'), StringComparison.OrdinalIgnoreCase).ShouldBeTrue(type);
        type.Any(char.IsAsciiLetterUpper).ShouldBeFalse(type);
        problem.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("instance").GetString().ShouldStartWith("/");

        response.Body.ShouldNotContain("Exception", Case.Sensitive);
        response.Body.ShouldNotContain("   at ", Case.Sensitive);
        response.Body.ShouldNotContain("SELECT ", Case.Insensitive);
        response.Body.ShouldNotContain("Npgsql", Case.Insensitive);

        return problem;
    }

    public static IReadOnlyList<(string Field, string Reason)> ShouldBeValidationProblem(this ApiResponse response)
    {
        var problem = response.ShouldBeProblem(400, "VALIDATION_FAILED");

        problem.GetProperty("detail").GetString().ShouldBe("Um ou mais campos são inválidos.");

        return
        [
            .. problem.GetProperty("errors").EnumerateArray()
                .Select(item => (item.GetProperty("field").GetString() ?? string.Empty,
                    item.GetProperty("reason").GetString() ?? string.Empty))
        ];
    }

    public static IReadOnlyList<string> DescriptiveTexts(this ApiResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var problem = response.Json();
        var texts = new List<string>();

        foreach (var name in DescriptiveProperties)
        {
            if (problem.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                texts.Add(value.GetString() ?? string.Empty);
            }
        }

        if (!problem.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
        {
            return texts;
        }

        foreach (var error in errors.EnumerateArray())
        {
            foreach (var property in error.EnumerateObject().Where(item => item.Value.ValueKind == JsonValueKind.String))
            {
                texts.Add(property.Value.GetString() ?? string.Empty);
            }
        }

        return texts;
    }

    public static void ShouldBeUuidText(this string value)
    {
        UuidShape().IsMatch(value).ShouldBeTrue(value);
    }

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex UuidShape();
}
