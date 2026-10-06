namespace Ledger.Architecture.Tests.Hygiene;

[Trait("Category", "Architecture")]
public sealed class RoslynSyntaxGuardTests
{
    private static readonly string[] ProductionProjects =
    [
        "Ledger.Domain",
        "Ledger.Application",
        "Ledger.Infrastructure",
        "Ledger.Api",
        "Ledger.Worker"
    ];

    [Fact]
    public void Scan_CoversEveryProductionProjectAndTheTests()
    {
        var production = SourceFiles.Under("src").Select(SourceFiles.RelativeToRepository).ToList();
        var tests = SourceFiles.Under("tests").Select(SourceFiles.RelativeToRepository).ToList();

        foreach (var project in ProductionProjects)
        {
            production.ShouldContain(path => path.StartsWith($"src/{project}/", StringComparison.Ordinal));
        }

        tests.ShouldContain(path => path.StartsWith("tests/Ledger.Architecture.Tests/", StringComparison.Ordinal));
        production.ShouldNotContain(path => path.Contains("/obj/") || path.Contains("/bin/"));
    }

    [Fact]
    public void SourceAndTests_HaveNoPragmaDirectives()
    {
        var offenders = Offenders(["src", "tests"], HygieneScanner.PragmaLines);

        offenders.ShouldBeEmpty(Describe("pragma directive", offenders));
    }

    [Fact]
    public void Source_HasNoNullForgivingOperator()
    {
        var offenders = Offenders(["src"], HygieneScanner.NullForgivingLines);

        offenders.ShouldBeEmpty(Describe("null-forgiving operator", offenders));
    }

    [Theory]
    [InlineData("#pragma warning disable CS0168\nclass C { }", 1)]
    [InlineData("class C { }\n#pragma warning restore CS0168", 2)]
    [InlineData("#pragma warning disable\nclass C { }", 1)]
    [InlineData("#pragma checksum \"a.cs\" \"{00000000-0000-0000-0000-000000000000}\" \"00\"\nclass C { }", 1)]
    public void PragmaLines_FlagsAPlantedDirective(string source, int expectedLine)
    {
        HygieneScanner.PragmaLines(source).ShouldContain(expectedLine);
    }

    [Theory]
    [InlineData("#nullable disable\nclass C { }")]
    [InlineData("#region Members\nclass C { }\n#endregion")]
    [InlineData("class C { string S = \"#pragma warning disable CS0168\"; }")]
    public void PragmaLines_IgnoresLookalikes(string source)
    {
        HygieneScanner.PragmaLines(source).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("class C { string? S; int M() => S!.Length; }", 1)]
    [InlineData("class C { string S = null!; }", 1)]
    [InlineData("class C { string S = default!; }", 1)]
    [InlineData("class C { string? Get() => null; string M() => Get()!; }", 1)]
    [InlineData("class C\n{\n    async System.Threading.Tasks.Task<string?> G() => null;\n    async System.Threading.Tasks.Task<string> M() => (await G())!;\n}", 4)]
    public void NullForgivingLines_FlagsAPlantedOperator(string source, int expectedLine)
    {
        HygieneScanner.NullForgivingLines(source).ShouldContain(expectedLine);
    }

    [Theory]
    [InlineData("class C { bool M(int a, int b) => a != b; }")]
    [InlineData("class C { bool M(bool flag) => !flag; }")]
    [InlineData("class C { bool M(string? s) => s is not null; }")]
    [InlineData("class C { bool M(int a, int b) => a!=b; }")]
    [InlineData("class C { string S = \"Hello!\"; }")]
    [InlineData("class C { bool M(bool ok) { if (!ok) { return false; } return true; } }")]
    public void NullForgivingLines_IgnoresLookalikes(string source)
    {
        HygieneScanner.NullForgivingLines(source).ShouldBeEmpty();
    }

    private static List<string> Offenders(string[] folders, Func<string, int[]> scan)
    {
        return folders
            .SelectMany(SourceFiles.Under)
            .SelectMany(path =>
                scan(File.ReadAllText(path)).Select(line => $"{SourceFiles.RelativeToRepository(path)}:{line}"))
            .ToList();
    }

    private static string Describe(string kind, List<string> offenders)
    {
        return $"Found a {kind} in: {string.Join(", ", offenders)}";
    }
}
