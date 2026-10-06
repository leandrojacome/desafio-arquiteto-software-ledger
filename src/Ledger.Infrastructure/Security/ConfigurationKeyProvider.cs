using Ledger.Application.Abstractions;
using Ledger.Application.Security;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Security;

internal sealed class ConfigurationKeyProvider : IKeyProvider
{
    private readonly KeySnapshot _snapshot;

    public ConfigurationKeyProvider(IOptions<PiiOptions> options)
    {
        _snapshot = ConfigurationKeySets.Build(options.Value);
    }

    public bool IsAvailable => true;

    public KeySet Active => _snapshot.Active;

    public IReadOnlyCollection<KeySet> Live => _snapshot.Live;

    public IReadOnlyCollection<ushort> VanishedVersions => [];

    public KeySet Get(ushort version)
    {
        return _snapshot.TryGet(version, out var keys)
            ? keys
            : throw new KeyNotFoundException($"No key set with version {version} is loaded.");
    }
}
