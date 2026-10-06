using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Ledger.Architecture.Tests.Ci;
using Ledger.Architecture.Tests.Layers;

namespace Ledger.Architecture.Tests.Hygiene;

internal static partial class DocumentedSql
{
    private const string FlowsFolder = "docs/06-fluxos/";

    private const BindingFlags AnyStaticField = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                                                | BindingFlags.DeclaredOnly;

    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> BlocksByPage = new(StringComparer.Ordinal);

    public static string Normalize(string sql)
    {
        return Whitespace().Replace(sql, " ").Trim();
    }

    public static string ConstantNamed(string name)
    {
        var values = LayerAssemblies.Infrastructure
            .GetTypes()
            .SelectMany(type => type.GetFields(AnyStaticField))
            .Where(field => field.Name == name && field.FieldType == typeof(string))
            .Select(field => field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        values.Count.ShouldBe(1, $"{name} must be declared exactly once as a string in Ledger.Infrastructure");

        return values[0];
    }

    public static bool AppearsInPage(string sql, string page)
    {
        var code = Normalize(sql);

        return BlocksOf(page).Any(block => block.Contains(code, StringComparison.Ordinal));
    }

    public static void ShouldAppearInPage(string constant, string page)
    {
        BlocksOf(page).ShouldNotBeEmpty($"{FlowsFolder}{page} has no sql block");

        AppearsInPage(ConstantNamed(constant), page).ShouldBeTrue(
            $"{constant} is not in any sql block of {FlowsFolder}{page}. " +
            "Change the constant and the block together, normalized by whitespace.");
    }

    private static IReadOnlyList<string> BlocksOf(string page)
    {
        return BlocksByPage.GetOrAdd(page, LoadBlocks);
    }

    private static List<string> LoadBlocks(string page)
    {
        var blocks = new List<string>();
        var inside = false;
        var current = new List<string>();

        foreach (var line in RepositoryFiles.ReadAllText(FlowsFolder + page).ReplaceLineEndings("\n").Split('\n'))
        {
            if (inside)
            {
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    blocks.Add(Normalize(string.Join('\n', current)));
                    current.Clear();
                    inside = false;
                }
                else
                {
                    current.Add(line);
                }

                continue;
            }

            if (line.StartsWith("```sql", StringComparison.Ordinal))
            {
                inside = true;
            }
        }

        return blocks;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
