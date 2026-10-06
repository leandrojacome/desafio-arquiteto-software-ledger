using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Ledger.Application.Security;

namespace Ledger.Infrastructure.Security;

internal sealed class KeySnapshot
{
    private readonly Dictionary<ushort, KeySet> _byVersion;

    public KeySnapshot(IReadOnlyCollection<KeySet> sets, ushort activeVersion)
    {
        _byVersion = sets.ToDictionary(set => set.Version);
        Live = [.. sets.OrderBy(set => set.Version)];
        Active = _byVersion[activeVersion];
    }

    public KeySet Active { get; }

    public IReadOnlyCollection<KeySet> Live { get; }

    public bool TryGet(ushort version, [NotNullWhen(true)] out KeySet? keys)
    {
        return _byVersion.TryGetValue(version, out keys);
    }

    public IReadOnlyList<ushort> VersionsMissingFrom(KeySnapshot previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        return [.. previous.Live.Select(set => set.Version).Where(version => !_byVersion.ContainsKey(version))];
    }

    public IReadOnlyList<ushort> VersionsWithOtherKeysThan(KeySnapshot previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        return
        [
            .. previous.Live
                .Where(old => _byVersion.TryGetValue(old.Version, out var current) && !SameKeys(old, current))
                .Select(old => old.Version)
        ];
    }

    private static bool SameKeys(KeySet first, KeySet second)
    {
        return CryptographicOperations.FixedTimeEquals(first.EncryptionKey.Span, second.EncryptionKey.Span)
               & CryptographicOperations.FixedTimeEquals(first.BlindIndexKey.Span, second.BlindIndexKey.Span);
    }
}
