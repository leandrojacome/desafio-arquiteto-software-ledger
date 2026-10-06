namespace Ledger.Architecture.Tests.Ci;

internal static class RepositoryFiles
{
    private const string SolutionFile = "Ledger.sln";

    private static string Root { get; } = FindRoot();

    public static string PathOf(string relativePath)
    {
        return Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static bool Exists(string relativePath)
    {
        return File.Exists(PathOf(relativePath)) || Directory.Exists(PathOf(relativePath));
    }

    public static string ReadAllText(string relativePath)
    {
        return File.ReadAllText(PathOf(relativePath));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
               throw new InvalidOperationException($"{SolutionFile} was not found above {AppContext.BaseDirectory}.");
    }
}
