using System.Text.RegularExpressions;

namespace Ledger.Architecture.Tests.Hygiene;

[Trait("Category", "Architecture")]
public sealed partial class CryptographyBoundaryTests
{
    private const string SecurityFolder = "src/Ledger.Infrastructure/Security/";

    [GeneratedRegex(@"\b(AesGcm|HMACSHA256|RandomNumberGenerator)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Primitives();

    private static bool IsInsideSecurityFolder(string file)
    {
        return SourceFiles.RelativeToRepository(file).StartsWith(SecurityFolder, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CipherMacAndKeyRandomnessPrimitives_LiveOnlyInTheInfrastructureSecurityFolder()
    {
        var offenders = SourceFiles.Under("src")
            .Where(file => !IsInsideSecurityFolder(file))
            .Where(file => Primitives().IsMatch(File.ReadAllText(file)))
            .Select(SourceFiles.RelativeToRepository)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void TheSecurityFolder_DoesUseThePrimitives()
    {
        var users = SourceFiles.Under("src")
            .Where(IsInsideSecurityFolder)
            .Where(file => Primitives().IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .ToList();

        users.ShouldContain("AesGcmDocumentCipher.cs");
        users.ShouldContain("HmacBlindIndex.cs");
        users.ShouldContain("HmacStatementCursorProtector.cs");
    }

    [Theory]
    [InlineData("using var aes = new AesGcm(key, 16);")]
    [InlineData("var mac = HMACSHA256.HashData(key, data);")]
    [InlineData("RandomNumberGenerator.Fill(nonce);")]
    public void Detector_RecognizesEachForbiddenPrimitive(string line)
    {
        Primitives().IsMatch(line).ShouldBeTrue();
    }

    [Theory]
    [InlineData("var cipher = new AesGcmDocumentCipher();")]
    [InlineData("var hash = SHA256.HashData(bytes);")]
    [InlineData("HmacBlindIndex.Compute(key, text);")]
    public void Detector_IgnoresNamesThatOnlyContainAPrimitiveAsAPrefix(string line)
    {
        Primitives().IsMatch(line).ShouldBeFalse();
    }
}
