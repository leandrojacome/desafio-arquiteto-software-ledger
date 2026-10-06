using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Ledger.Infrastructure.Persistence;

internal static partial class SchemaVersion
{
    private const string MigrationsNamespace = "Ledger.Infrastructure.Persistence.Migrations.";

    public static int Expected { get; } = Highest(
        InfrastructureAssembly.Reference.GetManifestResourceNames()
            .Where(name => name.StartsWith(MigrationsNamespace, StringComparison.Ordinal)));

    public static int Highest(IEnumerable<string> scriptNames)
    {
        var highest = 0;

        foreach (var name in scriptNames)
        {
            var match = ScriptName().Match(name);

            if (match.Success)
            {
                highest = Math.Max(highest, int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture));
            }
        }

        return highest;
    }

    public static bool IsSatisfiedBy(int expected, int? current) => current is { } version && version >= expected;

    [GeneratedRegex(@"(?:^|\.)(?<version>\d{4})_[a-z0-9]+(?:_[a-z0-9]+)*\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptName();
}
