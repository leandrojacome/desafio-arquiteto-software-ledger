namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class TestConfiguration
{
    public const string Issuer = "https://idp.test";
    public const string Audience = "ledger-api";
    public const string SigningKey = "integration-tests-symmetric-signing-key-0123456789";

    public const string PiiEncryptionKeyOne = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    public const string PiiBlindIndexKeyOne = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";
    public const string PiiEncryptionKeyTwo = "QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIzNDU=";
    public const string PiiBlindIndexKeyTwo = "enl4d3Z1dHNycXBvbm1sa2ppaGdmZWRjYmEwOTg3NjU=";
    public const string CursorSigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    private const string TestPassword = "integration-tests-password";
    private const string ProvisioningWildcard = "*";

    public static Dictionary<string, string?> ForPostgres(string host, int port, string database, string username,
        string password)
    {
        var values = new Dictionary<string, string?>
        {
            ["Postgres:Host"] = host,
            ["Postgres:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Postgres:Database"] = database,
            ["Postgres:SslMode"] = "Disable",
            ["Postgres:IncludeErrorDetail"] = "true",
            ["Authentication:Mode"] = "LocalKey",
            ["Authentication:Issuer"] = Issuer,
            ["Authentication:Audience"] = Audience,
            ["Authentication:RequireHttpsMetadata"] = "false",
            ["Authentication:LocalKey:SigningKey"] = SigningKey,
            ["Authorization:AccountProvisioningClients:0"] = ProvisioningWildcard,
            ["RateLimiting:Enabled"] = "false",
            ["Security:Pii:Provider"] = "Configuration",
            ["Security:Pii:ActiveKeyVersion"] = "1",
            ["Security:Pii:KeySets:1:EncryptionKey"] = PiiEncryptionKeyOne,
            ["Security:Pii:KeySets:1:BlindIndexKey"] = PiiBlindIndexKeyOne,
            ["Security:Pii:KeySets:2:EncryptionKey"] = PiiEncryptionKeyTwo,
            ["Security:Pii:KeySets:2:BlindIndexKey"] = PiiBlindIndexKeyTwo,
            ["Security:Cursor:SigningKey"] = CursorSigningKey,
            ["RabbitMq:Host"] = "127.0.0.1",
            ["RabbitMq:Port"] = "1",
            ["RabbitMq:Username"] = "ledger_worker",
            ["RabbitMq:Password"] = "integration-tests-broker-password",
            ["Serilog:MinimumLevel:Default"] = "Warning"
        };

        foreach (var source in new[] { "Write", "Balance", "Statement", "Worker", "Migrator" })
        {
            values[$"Postgres:Sources:{source}:Username"] = username;
            values[$"Postgres:Sources:{source}:Password"] = password;
        }

        return values;
    }

    public static Dictionary<string, string?> ForUnreachablePostgres()
    {
        var values = ForPostgres("127.0.0.1", 1, "ledger", "ledger_api", TestPassword);

        foreach (var source in new[] { "Write", "Balance", "Statement", "Worker", "Migrator" })
        {
            values[$"Postgres:Sources:{source}:MinPoolSize"] = "0";
        }

        return values;
    }
}
