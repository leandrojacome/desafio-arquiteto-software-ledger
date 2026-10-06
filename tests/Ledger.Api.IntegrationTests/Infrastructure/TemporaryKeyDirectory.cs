namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class TemporaryKeyDirectory : IDisposable
{
    private TemporaryKeyDirectory(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public static TemporaryKeyDirectory WithVersionOne()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ledger-keys-{Guid.NewGuid():N}");
        var version = Directory.CreateDirectory(Path.Combine(root, "1"));

        File.WriteAllText(Path.Combine(version.FullName, "encryption.key"), TestConfiguration.PiiEncryptionKeyOne);
        File.WriteAllText(Path.Combine(version.FullName, "blind-index.key"), TestConfiguration.PiiBlindIndexKeyOne);

        return new TemporaryKeyDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
