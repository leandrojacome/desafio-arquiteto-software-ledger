namespace Ledger.EndToEnd.Tests.Support;

internal static class E2EPaths
{
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ledger.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Ledger.sln was not found above the test assembly.");
    }

    public static string MigrationsDirectory() =>
        Path.Combine(RepositoryRoot(), "src", "Ledger.Infrastructure", "Persistence", "Migrations");
}

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
