using System.Text.RegularExpressions;
using Ledger.Architecture.Tests.Ci;
using Ledger.Architecture.Tests.Hygiene;

namespace Ledger.Architecture.Tests.Traceability;

[Trait("Category", "Architecture")]
public sealed partial class RequirementsMatrixTests
{
    private const string Requirements = "docs/02-contexto-e-requisitos/requisitos-funcionais.md";

    private const string Matrix = "docs/02-contexto-e-requisitos/rastreabilidade.md";

    private static readonly string[] CitingFiles = ["README.md", "docker/README.md"];

    private static readonly string[] CitingFolders = ["docs"];

    private static readonly HashSet<string> ProjectSuffixes = TestProjectSuffixes();

    [GeneratedRegex(@"^\*\*(FR-\d{2}) ", RegexOptions.Multiline)]
    private static partial Regex RequirementHeading();

    [GeneratedRegex(@"^\|\s*(FR-\d{2})\s*\|\s*`(?<class>[A-Za-z0-9_]+)(?:\.(?<method>[A-Za-z0-9_]+))?`\s*\|\s*(?<project>[A-Za-z0-9_.]+)\s*\|\s*$", RegexOptions.Multiline)]
    private static partial Regex MatrixRow();

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Z][A-Za-z0-9]*Tests)\b")]
    private static partial Regex CitedTestClass();

    [GeneratedRegex(@"\b(?:class|record|struct)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex TypeDeclaration();

    [Fact]
    public void TheRequirementsPageDeclaresTheRequirementsTheMatrixIsBuiltAgainst()
    {
        DeclaredRequirements().Count.ShouldBeGreaterThanOrEqualTo(24);
    }

    [Fact]
    public void EveryRequirementOfTheRequirementsPageHasAtLeastOneTestInTheMatrix()
    {
        var covered = Rows().Select(row => row.Requirement).ToHashSet(StringComparer.Ordinal);

        var missing = DeclaredRequirements().Where(requirement => !covered.Contains(requirement)).ToList();

        missing.ShouldBeEmpty($"requirements without a row in {Matrix}: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryRowOfTheMatrixNamesARequirementTheRequirementsPageHas()
    {
        var known = DeclaredRequirements().ToHashSet(StringComparer.Ordinal);

        var unknown = Rows().Where(row => !known.Contains(row.Requirement)).Select(row => row.Requirement).Distinct().ToList();

        unknown.ShouldBeEmpty($"rows of {Matrix} for requirements that {Requirements} does not declare: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void EveryTestOfTheMatrixStillExistsInTheSourcesOfItsProject()
    {
        var broken = new List<string>();

        foreach (var row in Rows())
        {
            var problem = Resolve(row);

            if (problem is not null)
            {
                broken.Add($"{row.Requirement} -> {row.TestClass}{(row.TestMethod is null ? string.Empty : "." + row.TestMethod)} in {row.Project}: {problem}");
            }
        }

        broken.ShouldBeEmpty($"tests of {Matrix} that no longer exist:{Environment.NewLine}{string.Join(Environment.NewLine, broken)}");
    }

    [Fact]
    public void NoRowIsRepeated()
    {
        var rows = Rows().Select(row => $"{row.Requirement}|{row.TestClass}|{row.TestMethod}|{row.Project}").ToList();

        rows.ShouldBeUnique();
    }

    [Fact]
    public void EveryTestClassTheDocumentsCiteStillExists()
    {
        var declared = DeclaredTestTypes();
        var broken = new List<string>();

        foreach (var document in CitingDocuments())
        {
            var missing = CitedClasses(File.ReadAllText(document)).Where(name => !declared.Contains(name)).Distinct(StringComparer.Ordinal);

            broken.AddRange(missing.Select(name => $"{SourceFiles.RelativeToRepository(document)}: {name}"));
        }

        broken.ShouldBeEmpty($"test classes cited by the pages of docs/ and by the READMEs that do not exist in tests/:{Environment.NewLine}{string.Join(Environment.NewLine, broken)}");
    }

    [Fact]
    public void TheDocumentationPagesAndTheReadmesAreAmongTheDocumentsThatCiteTestClasses()
    {
        var documents = CitingDocuments().Select(SourceFiles.RelativeToRepository).ToList();

        documents.ShouldContain(Matrix);
        documents.ShouldContain(path => path.StartsWith("docs/09-qualidade/", StringComparison.Ordinal));
        documents.ShouldContain("README.md");
        documents.ShouldContain("docker/README.md");
    }

    [Theory]
    [InlineData("O `ImaginaryTests` prova isso.", "ImaginaryTests")]
    [InlineData("tests/Ledger.Api.IntegrationTests/Writes/WriteTests.cs", "WriteTests")]
    [InlineData("Fica em Ledger.Api.IntegrationTests e em Ledger.Domain.Tests.", "")]
    [InlineData("Classes do tipo *Tests e a palavra Tests.", "")]
    [InlineData("dotnet test --filter \"FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~EndToEnd\"", "")]
    [InlineData("dotnet test --filter \"FullyQualifiedName~OpenApiContractTests\"", "OpenApiContractTests")]
    [InlineData("Primeira linha com `FirstTests`.\nSegunda linha com `SecondTests`.", "FirstTests,SecondTests")]
    public void TheCitationReader_FindsClassNamesAndSkipsProjectNames(string text, string expected)
    {
        var found = string.Join(",", CitedClasses(text));

        found.ShouldBe(expected);
    }

    private static IEnumerable<string> CitedClasses(string text)
    {
        return CitedTestClass().Matches(text)
            .Select(match => match.Groups["name"].Value)
            .Where(name => !ProjectSuffixes.Contains(name));
    }

    private static HashSet<string> TestProjectSuffixes()
    {
        return Directory.EnumerateDirectories(RepositoryFiles.PathOf("tests"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Select(name => name[(name.LastIndexOf('.') + 1)..])
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> CitingDocuments()
    {
        var documents = CitingFolders.SelectMany(folder =>
            Directory.EnumerateFiles(RepositoryFiles.PathOf(folder), "*.md", SearchOption.AllDirectories));

        return CitingFiles.Select(RepositoryFiles.PathOf).Concat(documents).Order(StringComparer.Ordinal);
    }

    private static HashSet<string> DeclaredTestTypes()
    {
        return SourceFiles
            .Under("tests")
            .SelectMany(path => TypeDeclaration().Matches(File.ReadAllText(path)).Select(match => match.Groups["name"].Value))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static List<string> DeclaredRequirements()
    {
        return [.. RequirementHeading().Matches(RepositoryFiles.ReadAllText(Requirements)).Select(match => match.Groups[1].Value).Distinct()];
    }

    private static List<MatrixEntry> Rows()
    {
        return [.. MatrixRow().Matches(RepositoryFiles.ReadAllText(Matrix)).Select(match => new MatrixEntry(
            match.Groups[1].Value,
            match.Groups["class"].Value,
            match.Groups["method"].Success ? match.Groups["method"].Value : null,
            match.Groups["project"].Value))];
    }

    private static string? Resolve(MatrixEntry row)
    {
        var folder = $"tests/{row.Project}";

        if (!RepositoryFiles.Exists(folder))
        {
            return "the project folder does not exist";
        }

        var declaration = new Regex($@"\b(class|record)\s+{Regex.Escape(row.TestClass)}\b", RegexOptions.None, TimeSpan.FromSeconds(5));
        var files = SourceFiles.Under(folder)
            .Where(path => declaration.IsMatch(File.ReadAllText(path)))
            .ToList();

        if (files.Count == 0)
        {
            return "no class with that name";
        }

        if (row.TestMethod is null)
        {
            return null;
        }

        var method = new Regex($@"\b{Regex.Escape(row.TestMethod)}\s*\(", RegexOptions.None, TimeSpan.FromSeconds(5));

        return files.Any(path => method.IsMatch(File.ReadAllText(path))) ? null : "no method with that name";
    }

    private sealed record MatrixEntry(string Requirement, string TestClass, string? TestMethod, string Project);
}
