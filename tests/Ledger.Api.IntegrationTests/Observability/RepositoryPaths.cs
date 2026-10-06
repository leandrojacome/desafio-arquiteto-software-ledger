namespace Ledger.Api.IntegrationTests.Observability;

internal static class RepositoryPaths
{
    private const string SolutionFile = "Ledger.sln";

    public static string Root { get; } = Locate();

    public static string Source { get; } = Path.Combine(Root, "src");

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.EnumerateFiles(SolutionFile).Any())
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root could not be located from the test output.");
    }
}
