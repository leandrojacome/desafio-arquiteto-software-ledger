using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Ledger.Architecture.Tests.Ci;

namespace Ledger.Architecture.Tests.Hygiene;

[Trait("Category", "Architecture")]
public sealed partial class MigrationChecksumTests
{
    private const string Folder = "src/Ledger.Infrastructure/Persistence/Migrations";

    private const string ChecksumFile = Folder + "/migrations.sha256";

    [GeneratedRegex(@"^(?<hash>[0-9a-f]{64})  (?<name>\S+\.sql)$", RegexOptions.Multiline)]
    private static partial Regex ChecksumLine();

    [Fact]
    public void EveryMigrationScript_HasItsLineInTheChecksumFile()
    {
        var listed = Recorded().Keys.ToHashSet(StringComparer.Ordinal);

        var missing = Scripts().Where(name => !listed.Contains(name)).ToList();

        missing.ShouldBeEmpty($"scripts without a line in {ChecksumFile}: {string.Join(", ", missing)}. Append the line of a new script, never change an old one.");
    }

    [Fact]
    public void EveryLineOfTheChecksumFile_NamesAScriptThatStillExists()
    {
        var existing = Scripts().ToHashSet(StringComparer.Ordinal);

        var orphans = Recorded().Keys.Where(name => !existing.Contains(name)).ToList();

        orphans.ShouldBeEmpty($"lines of {ChecksumFile} for scripts that no longer exist: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void ARecordedMigrationScript_IsNeverEdited()
    {
        var existing = Scripts();
        var edited = Recorded()
            .Where(line => existing.Contains(line.Key, StringComparer.Ordinal))
            .Where(line => !string.Equals(HashOfScript(line.Key), line.Value, StringComparison.Ordinal))
            .Select(line => line.Key)
            .ToList();

        edited.ShouldBeEmpty($"scripts changed after their checksum was recorded: {string.Join(", ", edited)}. A script already applied is never edited; write the next one.");
    }

    [Fact]
    public void TheChecksumFile_ListsTheScriptsInOrder()
    {
        var names = Recorded().Keys.ToList();

        names.ShouldBe([.. names.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void TheHash_IgnoresTheLineEndingsOfTheCheckout()
    {
        Checksum("SELECT 1;\r\nSELECT 2;\r\n").ShouldBe(Checksum("SELECT 1;\nSELECT 2;\n"));
    }

    private static List<string> Scripts()
    {
        return [.. Directory
            .EnumerateFiles(RepositoryFiles.PathOf(Folder), "*.sql")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)];
    }

    private static Dictionary<string, string> Recorded()
    {
        return ChecksumLine()
            .Matches(RepositoryFiles.ReadAllText(ChecksumFile).ReplaceLineEndings("\n"))
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["hash"].Value, StringComparer.Ordinal);
    }

    private static string HashOfScript(string script)
    {
        return Checksum(RepositoryFiles.ReadAllText($"{Folder}/{script}"));
    }

    private static string Checksum(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"))));
    }
}
