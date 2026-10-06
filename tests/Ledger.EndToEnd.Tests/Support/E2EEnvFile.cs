namespace Ledger.EndToEnd.Tests.Support;

internal static class E2EEnvFile
{
    public static IReadOnlyDictionary<string, string> Read()
    {
        var path = Path.Combine(E2EPaths.RepositoryRoot(), ".env");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!File.Exists(path))
        {
            return values;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var separator = line.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0 || line.StartsWith('#'))
            {
                continue;
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return values;
    }
}
