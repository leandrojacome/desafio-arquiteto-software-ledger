using System.Reflection;

namespace Ledger.Infrastructure.Observability;

internal static class BuildInfo
{
    private const string FallbackVersion = "0.0.0";
    private const char MetadataSeparator = '+';

    public static string Version { get; } = Resolve(typeof(BuildInfo).Assembly);

    public static string Normalize(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return FallbackVersion;
        }

        var separator = informationalVersion.IndexOf(MetadataSeparator, StringComparison.Ordinal);
        var version = separator < 0 ? informationalVersion : informationalVersion[..separator];

        return version.Length == 0 ? FallbackVersion : version;
    }

    private static string Resolve(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return Normalize(informational);
    }
}
