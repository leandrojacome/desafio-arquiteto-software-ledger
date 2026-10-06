namespace Ledger.Api.Security;

internal sealed class ProvisioningOptions
{
    public const string SectionName = "Authorization";

    public const string Wildcard = "*";

    public string[] AccountProvisioningClients { get; init; } = [];
}
