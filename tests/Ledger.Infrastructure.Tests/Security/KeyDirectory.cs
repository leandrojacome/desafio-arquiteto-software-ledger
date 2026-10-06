using System.Text;
using Ledger.Application.Tests.Security;

namespace Ledger.Infrastructure.Tests.Security;

internal sealed class KeyDirectory : IDisposable
{
    public KeyDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "ledger-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public KeyDirectory WithVersion(int version, string encryptionKeyBase64, string blindIndexKeyBase64)
    {
        var folder = Path.Combine(Root, version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "encryption.key"), encryptionKeyBase64 + "\n", Encoding.ASCII);
        File.WriteAllText(Path.Combine(folder, "blind-index.key"), blindIndexKeyBase64 + "\n", Encoding.ASCII);

        return this;
    }

    public KeyDirectory WithFolder(string folderName, string encryptionKeyBase64, string blindIndexKeyBase64)
    {
        var folder = Path.Combine(Root, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "encryption.key"), encryptionKeyBase64 + "\n", Encoding.ASCII);
        File.WriteAllText(Path.Combine(folder, "blind-index.key"), blindIndexKeyBase64 + "\n", Encoding.ASCII);

        return this;
    }

    public KeyDirectory WithDefaultVersionOne()
    {
        return WithVersion(1, SecurityVectors.EncryptionKeyBase64, SecurityVectors.BlindIndexKeyBase64);
    }

    public KeyDirectory WithDefaultVersionTwo()
    {
        return WithVersion(2, SecurityVectors.SecondEncryptionKeyBase64, SecurityVectors.SecondBlindIndexKeyBase64);
    }

    public void RemoveVersion(int version)
    {
        Directory.Delete(
            Path.Combine(Root, version.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            recursive: true);
    }

    public void DeleteFile(int version, string fileName)
    {
        File.Delete(Path.Combine(Root, version.ToString(System.Globalization.CultureInfo.InvariantCulture), fileName));
    }

    public void Overwrite(int version, string fileName, string content)
    {
        File.WriteAllText(
            Path.Combine(Root, version.ToString(System.Globalization.CultureInfo.InvariantCulture), fileName),
            content,
            Encoding.ASCII);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
