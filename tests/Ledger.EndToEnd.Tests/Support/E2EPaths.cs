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
