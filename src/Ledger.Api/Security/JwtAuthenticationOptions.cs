namespace Ledger.Api.Security;

internal sealed class JwtAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public AuthenticationMode Mode { get; init; } = AuthenticationMode.Authority;

    public string? Authority { get; init; }

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public bool RequireHttpsMetadata { get; init; } = true;

    public int MaxTokenLifetimeMinutes { get; init; } = 15;

    public bool RequireAccessTokenType { get; init; } = true;

    public LocalKeyOptions LocalKey { get; init; } = new();
}

internal sealed class LocalKeyOptions
{
    public string? SigningKey { get; init; }

    public string? PublicKeyPath { get; init; }
}

internal enum AuthenticationMode
{
    Authority,
    LocalKey
}
