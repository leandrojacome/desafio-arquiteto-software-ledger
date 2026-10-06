using System.Globalization;

namespace Ledger.Infrastructure.Security;

internal static class ConfigurationKeySets
{
    public static IReadOnlyList<RawKeySet> ToRawSets(PiiOptions options)
    {
        var raws = new List<RawKeySet>();

        foreach (var (versionText, keySet) in options.KeySets)
        {
            if (!ushort.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                || version == 0)
            {
                throw new KeyMaterialRejectedException(
                    KeyMaterialProblem.NoActiveSet,
                    $"{PiiOptions.SectionName}:KeySets has an entry whose name is not a version between 1 and 65535.");
            }

            var origin = $"{PiiOptions.SectionName}:KeySets:{versionText}";

            raws.Add(new RawKeySet(
                version,
                keySet.EncryptionKey ?? string.Empty,
                keySet.BlindIndexKey ?? string.Empty,
                $"{origin}:EncryptionKey",
                $"{origin}:BlindIndexKey"));
        }

        return raws;
    }

    public static KeySnapshot Build(PiiOptions options)
    {
        return KeySetReader.BuildSnapshot(
            ToRawSets(options),
            (ushort)options.ActiveKeyVersion,
            PiiOptionsValidator.ActiveVersionKey);
    }
}
