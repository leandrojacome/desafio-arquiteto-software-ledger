using System.Globalization;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Security;

internal sealed class DirectoryKeySetSource(IOptions<PiiOptions> options) : IKeySetSource
{
    public const string EncryptionFileName = "encryption.key";
    public const string BlindIndexFileName = "blind-index.key";

    public IReadOnlyList<RawKeySet> Load()
    {
        var root = options.Value.Directory;

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new KeySourceUnavailableException($"{PiiOptions.SectionName}:Directory is not configured.");
        }

        try
        {
            return ReadVersions(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new KeySourceUnavailableException("The key directory could not be read.", exception);
        }
    }

    private static List<RawKeySet> ReadVersions(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new KeySourceUnavailableException("The key directory does not exist.");
        }

        var sets = new List<RawKeySet>();

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);

            if (!ushort.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                || version == 0)
            {
                continue;
            }

            var encryptionPath = Path.Combine(directory, EncryptionFileName);
            var blindIndexPath = Path.Combine(directory, BlindIndexFileName);

            sets.Add(new RawKeySet(
                version,
                File.ReadAllText(encryptionPath),
                File.ReadAllText(blindIndexPath),
                encryptionPath,
                blindIndexPath));
        }

        if (sets.Count == 0)
        {
            throw new KeySourceUnavailableException("The key directory has no key set.");
        }

        return sets;
    }
}
