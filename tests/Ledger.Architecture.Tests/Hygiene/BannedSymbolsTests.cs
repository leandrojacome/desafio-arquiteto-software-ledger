using Ledger.Architecture.Tests.Ci;

namespace Ledger.Architecture.Tests.Hygiene;

[Trait("Category", "Architecture")]
public sealed class BannedSymbolsTests
{
    private const string GuidRule = "M:System.Guid.NewGuid";

    [Fact]
    public void TheSourceList_BansGuidNewGuid()
    {
        RuleLines("BannedSymbols.txt").ShouldContain(line => line.StartsWith(GuidRule + ";", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTestsList_IsTheSourceListWithoutTheGuidRuleAndNothingElse()
    {
        var source = RuleLines("BannedSymbols.txt")
            .Where(line => !line.StartsWith(GuidRule + ";", StringComparison.Ordinal))
            .ToList();
        var tests = RuleLines("tests/BannedSymbols.txt");

        tests.ShouldBe(source);
    }

    [Fact]
    public void NoSourceFile_CallsGuidNewGuid()
    {
        var offenders = SourceFiles.Under("src")
            .Where(path => File.ReadAllText(path).Contains("Guid.NewGuid", StringComparison.Ordinal))
            .Select(SourceFiles.RelativeToRepository)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    private static List<string> RuleLines(string relativePath)
    {
        return [.. RepositoryFiles.ReadAllText(relativePath)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)];
    }
}
