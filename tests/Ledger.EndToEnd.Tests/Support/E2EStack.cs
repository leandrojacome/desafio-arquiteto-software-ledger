namespace Ledger.EndToEnd.Tests.Support;

internal sealed class E2EStack(
    Uri apiUrl,
    Uri workerUrl,
    Uri managementUrl,
    ComposeControl compose,
    string? rabbitMqUser,
    string? rabbitMqPassword,
    string? postgresConnection,
    bool provisioned,
    string? postgresAdminConnection = null)
{
    private const string RabbitMqUserVariable = "LEDGER_E2E_RABBITMQ_USER";

    private const string RabbitMqPasswordVariable = "LEDGER_E2E_RABBITMQ_PASSWORD";

    private const string PostgresConnectionVariable = "LEDGER_E2E_POSTGRES_CONNECTION";

    private const string PostgresAdminConnectionVariable = "LEDGER_E2E_POSTGRES_ADMIN_CONNECTION";

    private const string PostgresPortVariable = "POSTGRES_PORT";

    private const string DefaultPostgresPort = "5432";

    public Uri ApiUrl { get; } = apiUrl;

    public Uri WorkerUrl { get; } = workerUrl;

    public ComposeControl Compose { get; } = compose;

    public bool Provisioned { get; } = provisioned;

    public static E2EStack Attach(Func<string, string?> variable)
    {
        var file = E2EEnvFile.Read();

        var user = variable(RabbitMqUserVariable)
                   ?? file.GetValueOrDefault("RABBITMQ_USERNAME")
                   ?? "ledger_worker";
        var password = variable(RabbitMqPasswordVariable) ?? file.GetValueOrDefault("RABBITMQ_PASSWORD");
        var connection = variable(PostgresConnectionVariable) ?? ConnectionFromFile(variable, file);
        var adminConnection = variable(PostgresAdminConnectionVariable) ?? AdminConnectionFromFile(variable, file);

        return new E2EStack(
            E2EEnvironment.ResolveBaseUrl(variable),
            E2EEnvironment.ResolveWorkerUrl(variable),
            E2EEnvironment.ResolveManagementUrl(variable),
            ComposeControl.ForRunningStack(),
            user,
            password,
            connection,
            provisioned: false,
            adminConnection);
    }

    public RabbitMqManagement RabbitMq() =>
        new(
            managementUrl,
            rabbitMqUser ?? throw Missing(RabbitMqUserVariable),
            rabbitMqPassword ?? throw Missing(RabbitMqPasswordVariable));

    public E2EDatabase Database() =>
        new(postgresConnection ?? throw Missing(PostgresConnectionVariable));

    public E2EDatabase AdminDatabase() =>
        new(postgresAdminConnection ?? throw Missing(PostgresAdminConnectionVariable));

    public HttpClient CreateWorkerClient() => E2EEnvironment.CreateClient(WorkerUrl);

    internal static string? ConnectionFromFile(Func<string, string?> variable, IReadOnlyDictionary<string, string> file)
    {
        if (!file.TryGetValue("LEDGER_WORKER_PASSWORD", out var password))
        {
            return null;
        }

        var port = PostgresPort(variable, file);

        return $"Host=127.0.0.1;Port={port};Database=ledger;Username=ledger_worker;Password={password};Pooling=false;Timeout=5;Command Timeout=30";
    }

    internal static string? AdminConnectionFromFile(Func<string, string?> variable, IReadOnlyDictionary<string, string> file)
    {
        if (!file.TryGetValue("POSTGRES_SUPERUSER_PASSWORD", out var password))
        {
            return null;
        }

        var port = PostgresPort(variable, file);

        return $"Host=127.0.0.1;Port={port};Database=ledger;Username=postgres;Password={password};Pooling=false;Timeout=5;Command Timeout=30";
    }

    private static string PostgresPort(Func<string, string?> variable, IReadOnlyDictionary<string, string> file)
    {
        var fromEnvironment = variable(PostgresPortVariable);

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        return file.GetValueOrDefault(PostgresPortVariable) ?? DefaultPostgresPort;
    }

    private static InvalidOperationException Missing(string variable) =>
        new($"{variable} is not set and the repository .env does not carry the value either. Copy .env.example to .env, export {variable}, or set LEDGER_E2E_PROVISION=true.");
}
