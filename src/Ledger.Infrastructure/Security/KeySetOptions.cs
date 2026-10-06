using Ledger.Application.Security;

namespace Ledger.Infrastructure.Security;

internal sealed class KeySetOptions
{
    [Sensitive] public string? EncryptionKey { get; init; }

    [Sensitive] public string? BlindIndexKey { get; init; }
}
