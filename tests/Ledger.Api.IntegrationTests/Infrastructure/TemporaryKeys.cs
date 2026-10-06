using System.Security.Cryptography;

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

internal sealed class TemporaryPublicKey : IDisposable
{
    private TemporaryPublicKey(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TemporaryPublicKey Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ledger-public-key-{Guid.NewGuid():N}.pem");

        using var rsa = RSA.Create(2048);

        File.WriteAllText(path, rsa.ExportSubjectPublicKeyInfoPem());

        return new TemporaryPublicKey(path);
    }

    public void Dispose()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }
}
