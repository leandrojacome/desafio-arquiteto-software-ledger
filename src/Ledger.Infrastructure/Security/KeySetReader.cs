using System.Globalization;
using System.Security.Cryptography;
using Ledger.Application.Security;

namespace Ledger.Infrastructure.Security;

internal static class KeySetReader
{
    public const int KeySize = 32;

    public static KeySet Read(RawKeySet raw)
    {
        var encryption = Decode(raw.EncryptionKey, raw.EncryptionKeyOrigin);
        var blindIndex = Decode(raw.BlindIndexKey, raw.BlindIndexKeyOrigin);

        if (CryptographicOperations.FixedTimeEquals(encryption, blindIndex))
        {
            throw new KeyMaterialRejectedException(
                KeyMaterialProblem.IdenticalKeys,
                $"{raw.EncryptionKeyOrigin} and {raw.BlindIndexKeyOrigin} must hold different keys.");
        }

        return new KeySet(raw.Version, encryption, blindIndex);
    }

    public static KeySnapshot BuildSnapshot(
        IEnumerable<RawKeySet> rawSets,
        ushort activeVersion,
        string activeVersionOrigin)
    {
        var sets = new List<KeySet>();

        foreach (var raw in rawSets)
        {
            if (sets.Exists(set => set.Version == raw.Version))
            {
                throw new KeyMaterialRejectedException(
                    KeyMaterialProblem.DuplicateVersion,
                    string.Create(CultureInfo.InvariantCulture, $"More than one key set was found for version {raw.Version}."));
            }

            sets.Add(Read(raw));
        }

        if (sets.All(set => set.Version != activeVersion))
        {
            throw new KeyMaterialRejectedException(
                KeyMaterialProblem.NoActiveSet,
                NoActiveSetMessage(activeVersion, activeVersionOrigin));
        }

        return new KeySnapshot(sets, activeVersion);
    }

    public static string NoActiveSetMessage(ushort activeVersion, string activeVersionOrigin)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{activeVersionOrigin} is {activeVersion}, but no key set with that version was found.");
    }

    private static byte[] Decode(string text, string origin)
    {
        var buffer = new byte[(text.Length * 3 / 4) + 3];

        if (string.IsNullOrWhiteSpace(text) || !Convert.TryFromBase64String(text.Trim(), buffer, out var written))
        {
            throw new KeyMaterialRejectedException(
                KeyMaterialProblem.BadBase64,
                $"{origin} must be a base64 text.");
        }

        if (written != KeySize)
        {
            CryptographicOperations.ZeroMemory(buffer);

            throw new KeyMaterialRejectedException(
                KeyMaterialProblem.BadLength,
                string.Create(CultureInfo.InvariantCulture, $"{origin} must decode to exactly {KeySize} bytes."));
        }

        var key = buffer.AsSpan(0, written).ToArray();
        CryptographicOperations.ZeroMemory(buffer);

        return key;
    }
}
