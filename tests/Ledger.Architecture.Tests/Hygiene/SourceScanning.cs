using Ledger.Architecture.Tests.Ci;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

internal static class HygieneScanner
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly SyntaxKind[] PragmaKinds =
    [
        SyntaxKind.PragmaWarningDirectiveTrivia,
        SyntaxKind.PragmaChecksumDirectiveTrivia
    ];

    public static int[] PragmaLines(string source)
    {
        return LinesOfTrivia(source, PragmaKinds);
    }

    public static int[] NullForgivingLines(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot();

        return root
            .DescendantNodes()
            .Where(node => node.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            .Select(node => LineOf(node.GetLocation()))
            .ToArray();
    }

    private static int[] LinesOfTrivia(string source, SyntaxKind[] kinds)
    {
        var root = CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot();

        return root
            .DescendantTrivia()
            .Where(trivia => kinds.Contains(trivia.Kind()))
            .Select(trivia => LineOf(trivia.GetLocation()))
            .ToArray();
    }

    private static int LineOf(Location location)
    {
        return location.GetLineSpan().StartLinePosition.Line + 1;
    }
}
