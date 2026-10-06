namespace Ledger.Api.Security;

internal sealed class ForwardedHeadersSettings
{
    public const string SectionName = "Security:ForwardedHeaders";

    public string[] KnownNetworks { get; init; } = [];
}
