namespace Ledger.Infrastructure.Tests.Observability.Support;

internal static class RepositoryRoot
{
    private const string SolutionFile = "Ledger.sln";

    public static string Path { get; } = Locate();

    public static string File(params string[] segments) =>
        System.IO.Path.Combine([Path, .. segments]);

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
