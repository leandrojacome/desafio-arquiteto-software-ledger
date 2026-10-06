using Ledger.Application.Security;

namespace Ledger.Application.Abstractions;

public interface IKeyProvider
{
    bool IsAvailable { get; }

    KeySet Active { get; }

    IReadOnlyCollection<KeySet> Live { get; }

    IReadOnlyCollection<ushort> VanishedVersions { get; }

    KeySet Get(ushort version);
}
