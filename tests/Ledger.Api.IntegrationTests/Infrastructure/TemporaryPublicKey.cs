using System.Security.Cryptography;

namespace Ledger.Api.IntegrationTests.Infrastructure;

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
