namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class ProductionLikeSettings
{
    public const string Authority = "https://auth.bank.internal";

    public const string ProvisioningClient = "pix-gateway";

    public static Dictionary<string, string?> Create(TemporaryKeyDirectory keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var settings = TestConfiguration.ForUnreachablePostgres();

        settings.Remove("Serilog:MinimumLevel:Default");

        settings["Authentication:Mode"] = "Authority";
        settings["Authentication:Authority"] = Authority;
        settings["Authentication:RequireHttpsMetadata"] = "true";
        settings["Authorization:AccountProvisioningClients:0"] = ProvisioningClient;
        settings["RateLimiting:Enabled"] = "true";
        settings["Postgres:SslMode"] = "VerifyFull";
        settings["Postgres:IncludeErrorDetail"] = "false";
        settings["Security:Pii:Provider"] = "Directory";
        settings["Security:Pii:Directory"] = keys.Root;
        settings["RabbitMq:UseTls"] = "true";

        return settings;
    }
}
