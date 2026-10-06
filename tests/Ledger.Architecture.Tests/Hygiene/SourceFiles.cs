using Ledger.Architecture.Tests.Ci;

namespace Ledger.Architecture.Tests.Hygiene;

internal static class SourceFiles
{
    private static readonly string[] BuildOutputFolders = ["bin", "obj"];

    public static IReadOnlyList<string> Under(string folder)
    {
        var root = RepositoryFiles.PathOf(folder);

        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(Path.GetRelativePath(root, path)))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public static string RelativeToRepository(string path)
    {
        return Path.GetRelativePath(RepositoryFiles.PathOf("."), path).Replace('\\', '/');
    }

    private static bool IsBuildOutput(string relativePath)
    {
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(segment => BuildOutputFolders.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }
}
