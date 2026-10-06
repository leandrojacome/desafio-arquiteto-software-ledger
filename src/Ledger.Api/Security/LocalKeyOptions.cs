namespace Ledger.Api.Security;

internal sealed class LocalKeyOptions
{
    public string? SigningKey { get; init; }

    public string? PublicKeyPath { get; init; }
}
