using Ledger.Application.Abstractions;
using Ledger.Application.Security;

namespace Ledger.Application.Tests.Security;

internal sealed class FakeKeyProvider : IKeyProvider
{
    private readonly KeySet[] _sets;
    private readonly ushort _activeVersion;

    public FakeKeyProvider(ushort activeVersion, params KeySet[] sets)
    {
        _activeVersion = activeVersion;
        _sets = sets;
        IsAvailable = true;
    }

    public bool IsAvailable { get; private set; }

    public KeySet Active => IsAvailable ? _sets.Single(set => set.Version == _activeVersion) : throw Unavailable();

    public IReadOnlyCollection<KeySet> Live => IsAvailable ? _sets : [];

    public IReadOnlyCollection<ushort> VanishedVersions { get; private set; } = [];

    public KeySet Get(ushort version)
    {
        if (!IsAvailable)
        {
            throw Unavailable();
        }

        return _sets.FirstOrDefault(set => set.Version == version)
               ?? throw new KeyNotFoundException($"No key set with version {version} is loaded.");
    }

    public FakeKeyProvider WithVanished(params ushort[] versions)
    {
        VanishedVersions = versions;

        return this;
    }

    public FakeKeyProvider WentDown()
    {
        IsAvailable = false;

        return this;
    }

    private static KeyProviderUnavailableException Unavailable() => new();
}
