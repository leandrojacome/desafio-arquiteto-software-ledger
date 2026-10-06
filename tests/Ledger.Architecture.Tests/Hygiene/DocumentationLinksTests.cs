using System.Text.RegularExpressions;
using Ledger.Architecture.Tests.Ci;

namespace Ledger.Architecture.Tests.Hygiene;

[Trait("Category", "Architecture")]
public sealed partial class DocumentationLinksTests
{
    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules", "TestResults"];

    [Fact]
    public void TheRepositoryHasMarkdownFilesWithRelativeLinksToCheck()
    {
        var files = MarkdownFiles(Root());

        files.Count.ShouldBeGreaterThan(20);
        files.SelectMany(file => RelativeLinks(File.ReadAllLines(file))).Count().ShouldBeGreaterThan(100);
    }

    [Fact]
    public void EveryRelativeLinkAndImageOfTheMarkdownFilesPointsToAnExistingFileAndHeading()
    {
        var broken = BrokenLinks(Root());

        broken.ShouldBeEmpty($"links that do not resolve:{Environment.NewLine}{string.Join(Environment.NewLine, broken)}");
    }

    [Fact]
    public void ALinkToAFileThatDoesNotExist_IsReported()
    {
        using var folder = new TempFolder();
        folder.Write("README.md", "# Início\n\nVeja [o guia](docs/guia.md) e [o que não existe](docs/ausente.md).\n");
        folder.Write("docs/guia.md", "# Guia\n");

        var broken = BrokenLinks(folder.Root);

        broken.ShouldHaveSingleItem();
        broken[0].ShouldStartWith("README.md:3:");
        broken[0].ShouldContain("docs/ausente.md");
    }

    [Fact]
    public void AnImageThatDoesNotExist_IsReported()
    {
        using var folder = new TempFolder();
        folder.Write("README.md", "# Início\n\n![desenho](figuras/desenho.png)\n");

        var broken = BrokenLinks(folder.Root);

        broken.ShouldHaveSingleItem();
        broken[0].ShouldContain("figuras/desenho.png");
    }

    [Fact]
    public void AHeadingThatDoesNotExistInTheTargetFile_IsReported()
    {
        using var folder = new TempFolder();
        folder.Write("README.md", "# Início\n\nVeja [a seção](docs/guia.md#seção-que-não-existe) e [a outra](docs/guia.md#segunda-seção).\n");
        folder.Write("docs/guia.md", "# Guia\n\n## Segunda seção\n");

        var broken = BrokenLinks(folder.Root);

        broken.ShouldHaveSingleItem();
        broken[0].ShouldStartWith("README.md:3:");
        broken[0].ShouldContain("#seção-que-não-existe");
    }

    [Fact]
    public void AHeadingThatDoesNotExistInTheSameFile_IsReported()
    {
        using var folder = new TempFolder();
        folder.Write("README.md", "# Início\n\n[ali](#outro-lugar) e [aqui](#início)\n");

        var broken = BrokenLinks(folder.Root);

        broken.ShouldHaveSingleItem();
        broken[0].ShouldContain("#outro-lugar");
    }

    [Fact]
    public void ALineInsideAFencedBlockIsNeverAHeading()
    {
        using var folder = new TempFolder();
        folder.Write("README.md", "# Início\n\n[comentário](docs/guia.md#comentário-do-script)\n");
        folder.Write("docs/guia.md", "# Guia\n\n```bash\n# comentário do script\n```\n");

        BrokenLinks(folder.Root).ShouldHaveSingleItem();
    }

    [Fact]
    public void LinksToExistingFilesFoldersImagesAndHeadings_AreAccepted()
    {
        using var folder = new TempFolder();
        folder.Write(
            "README.md",
            "# Título com Acentos e Pontuação!\n\n"
            + "[no mesmo arquivo](#título-com-acentos-e-pontuação) e [em outro](docs/guia.md#segunda-seção).\n"
            + "[repetido](docs/guia.md#repetido) e [o segundo repetido](docs/guia.md#repetido-1).\n"
            + "[com título](docs/guia.md \"O guia\") e [pasta](docs/) e [raiz](/docs/guia.md).\n"
            + "![figura](docs/figura.png) ![com título](docs/figura.png \"A figura\").\n"
            + "[com espaço](docs/meu%20guia.md#guia-com-espaço) e [entre colchetes](<docs/guia.md>).\n");
        folder.Write("docs/guia.md", "# Guia\n\n## Segunda `seção`\n\n## Repetido\n\n## Repetido\n\n[de volta](../README.md#título-com-acentos-e-pontuação)\n");
        folder.Write("docs/meu guia.md", "# Guia com espaço\n");
        folder.Write("docs/figura.png", "x");

        BrokenLinks(folder.Root).ShouldBeEmpty();
    }

    [Fact]
    public void ExternalLinksCodeSpansAndFencedBlocks_AreNotChecked()
    {
        using var folder = new TempFolder();
        folder.Write(
            "README.md",
            "# Início\n\n"
            + "[site](https://example.com/ausente.md#nada) e [e-mail](mailto:alguem@example.com).\n"
            + "O texto `[exemplo](ausente.md)` mostra a sintaxe.\n\n"
            + "```markdown\n[exemplo](ausente.md)\n```\n\n"
            + "~~~\n[exemplo](ausente.md)\n~~~\n");

        BrokenLinks(folder.Root).ShouldBeEmpty();
    }

    [Fact]
    public void HiddenFoldersAndBuildOutputAreNotWalked()
    {
        using var folder = new TempFolder();
        folder.Write("README.md", "# Início\n");

        foreach (var skipped in new[] { ".local", ".github", "bin", "obj", "node_modules", "TestResults" })
        {
            folder.Write($"{skipped}/nota.md", "[quebrado](ausente.md)\n");
        }

        MarkdownFiles(folder.Root).ShouldBe([Path.Combine(folder.Root, "README.md")]);
        BrokenLinks(folder.Root).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Visão Geral", "visão-geral")]
    [InlineData("Decisões: o que `mudou`?", "decisões-o-que-mudou")]
    [InlineData("A/B & C", "ab--c")]
    [InlineData("snake_case e kebab-case", "snake_case-e-kebab-case")]
    [InlineData("[Link](guia.md) no título", "link-no-título")]
    [InlineData("documento de arquitetura 0016: CI/CD", "documento-de-arquitetura-0016-cicd")]
    [InlineData("Idempotency-Key e Retry-After", "idempotency-key-e-retry-after")]
    public void TheSlugOfAHeading_FollowsTheRulesOfGitHub(string heading, string expected)
    {
        Slug(heading).ShouldBe(expected);
    }

    [Fact]
    public void TheClosingHashesOfAHeadingAreNotPartOfItsAnchor()
    {
        using var folder = new TempFolder();
        folder.Write("README.md", "# Início\n\n[ali](docs/guia.md#título) e [aqui](docs/guia.md#linguagem-c)\n");
        folder.Write("docs/guia.md", "## Título ##\n\n### Linguagem C#\n");

        BrokenLinks(folder.Root).ShouldBeEmpty();
    }

    private static string Root() => Path.GetFullPath(RepositoryFiles.PathOf("."));

    private static List<string> BrokenLinks(string root)
    {
        var headings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var broken = new List<string>();

        foreach (var file in MarkdownFiles(root))
        {
            foreach (var (line, target) in RelativeLinks(File.ReadAllLines(file)))
            {
                var problem = ProblemOf(root, file, target, headings);

                if (problem is not null)
                {
                    broken.Add($"{Path.GetRelativePath(root, file).Replace('\\', '/')}:{line}: {problem}");
                }
            }
        }

        return broken;
    }

    private static string? ProblemOf(string root, string file, string target, Dictionary<string, HashSet<string>> headings)
    {
        var hash = target.IndexOf('#', StringComparison.Ordinal);
        var path = Uri.UnescapeDataString(hash < 0 ? target : target[..hash]);
        var fragment = hash < 0 ? string.Empty : Uri.UnescapeDataString(target[(hash + 1)..]);

        var directory = Path.GetDirectoryName(file) ?? root;
        var destination = path.Length == 0
            ? file
            : Path.GetFullPath(path.StartsWith('/') ? Path.Combine(root, path.TrimStart('/')) : Path.Combine(directory, path));

        if (!File.Exists(destination) && !Directory.Exists(destination))
        {
            return $"{target} points to a file that does not exist";
        }

        if (fragment.Length == 0 || !File.Exists(destination) || !destination.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!headings.TryGetValue(destination, out var anchors))
        {
            anchors = AnchorsOf(File.ReadAllLines(destination));
            headings[destination] = anchors;
        }

        return anchors.Contains(Lowercase(fragment)) ? null : $"{target} points to a heading that does not exist";
    }

    private static List<string> MarkdownFiles(string root)
    {
        var files = new List<string>();

        Collect(root, files);

        return [.. files.Order(StringComparer.Ordinal)];
    }

    private static void Collect(string folder, List<string> files)
    {
        files.AddRange(Directory.EnumerateFiles(folder, "*.md"));

        foreach (var child in Directory.EnumerateDirectories(folder))
        {
            var name = Path.GetFileName(child);

            if (!name.StartsWith('.') && !SkippedFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                Collect(child, files);
            }
        }
    }

    private static IEnumerable<(int Line, string Text)> ProseLines(IReadOnlyList<string> lines)
    {
        var fence = string.Empty;

        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].TrimStart();

            if (fence.Length > 0)
            {
                if (trimmed.StartsWith(fence, StringComparison.Ordinal))
                {
                    fence = string.Empty;
                }

                continue;
            }

            var opening = Fence().Match(trimmed);

            if (opening.Success)
            {
                fence = opening.Groups["run"].Value;

                continue;
            }

            yield return (index + 1, lines[index]);
        }
    }

    private static IEnumerable<(int Line, string Target)> RelativeLinks(IReadOnlyList<string> lines)
    {
        foreach (var (line, text) in ProseLines(lines))
        {
            foreach (Match match in Link().Matches(CodeSpan().Replace(text, string.Empty)))
            {
                var target = match.Groups["target"].Value.Trim('<', '>');

                if (target.Length > 0 && !Scheme().IsMatch(target) && !target.StartsWith("//", StringComparison.Ordinal))
                {
                    yield return (line, target);
                }
            }
        }
    }

    private static HashSet<string> AnchorsOf(IReadOnlyList<string> lines)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, text) in ProseLines(lines))
        {
            var heading = Heading().Match(text);

            if (!heading.Success)
            {
                continue;
            }

            var slug = Slug(heading.Groups["text"].Value);
            var candidate = slug;
            var repeated = 0;

            while (!anchors.Add(candidate))
            {
                repeated++;
                candidate = $"{slug}-{repeated}";
            }
        }

        return anchors;
    }

    private static string Lowercase(string text) => new([.. text.Select(char.ToLowerInvariant)]);

    private static string Slug(string heading)
    {
        var text = InlineLink().Replace(heading, "${text}").Replace("`", string.Empty, StringComparison.Ordinal);

        return NotInSlug().Replace(Lowercase(text.Trim()), string.Empty).Replace(' ', '-');
    }

    [GeneratedRegex(@"!?\[[^\]]*\]\((?<target><[^>]*>|[^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"`[^`]*`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:")]
    private static partial Regex Scheme();

    [GeneratedRegex(@"^(?<run>`{3,}|~{3,})")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^ {0,3}#{1,6}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\[(?<text>[^\]]*)\]\([^)]*\)")]
    private static partial Regex InlineLink();

    [GeneratedRegex(@"[^\p{L}\p{M}\p{N}_\- ]")]
    private static partial Regex NotInSlug();

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ledger-links-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
