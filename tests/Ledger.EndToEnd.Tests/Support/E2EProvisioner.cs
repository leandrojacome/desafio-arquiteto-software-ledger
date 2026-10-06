using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Ledger.EndToEnd.Tests.Support;

internal static class E2EProvisioner
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private const string RabbitMqUser = "ledger_worker";

    public static async Task<E2EStack> UpAsync()
    {
        var token = RandomNumberGenerator.GetString("abcdef0123456789", 8);
        var project = $"ledger-e2e-{token}";
        var ports = new Ports(FreePort(), FreePort(), FreePort(), FreePort(), FreePort());
        var workerPassword = Password();
        var superuserPassword = Password();
        var rabbitMqPassword = Password();

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["POSTGRES_SUPERUSER_PASSWORD"] = superuserPassword,
            ["LEDGER_MIGRATOR_PASSWORD"] = Password(),
            ["LEDGER_API_PASSWORD"] = Password(),
            ["LEDGER_WORKER_PASSWORD"] = workerPassword,
            ["LEDGER_READONLY_PASSWORD"] = Password(),
            ["RABBITMQ_USERNAME"] = RabbitMqUser,
            ["RABBITMQ_PASSWORD"] = rabbitMqPassword,
            ["PII_ENCRYPTION_KEY"] = Key(),
            ["PII_BLIND_INDEX_KEY"] = Key(),
            ["CURSOR_SIGNING_KEY"] = Key(),
            ["DEV_JWT_PUBLIC_KEY_PEM_B64"] = E2ETokenFactory.UseGeneratedKey(),
            ["POSTGRES_PORT"] = Text(ports.Postgres),
            ["RABBITMQ_PORT"] = Text(ports.RabbitMq),
            ["RABBITMQ_MANAGEMENT_PORT"] = Text(ports.Management),
            ["API_PORT"] = Text(ports.Api),
            ["WORKER_HEALTH_PORT"] = Text(ports.Worker)
        };

        var compose = new ComposeControl(project, environment);

        try
        {
            await compose.UpAsync();
        }
        catch
        {
            await compose.DownAsync();

            throw;
        }

        return new E2EStack(
            new Uri($"http://127.0.0.1:{ports.Api}"),
            new Uri($"http://127.0.0.1:{ports.Worker}"),
            new Uri($"http://127.0.0.1:{ports.Management}"),
            compose,
            RabbitMqUser,
            rabbitMqPassword,
            $"Host=127.0.0.1;Port={ports.Postgres};Database=ledger;Username=ledger_worker;Password={workerPassword};Pooling=false;Timeout=5;Command Timeout=30",
            provisioned: true,
            $"Host=127.0.0.1;Port={ports.Postgres};Database=ledger;Username=postgres;Password={superuserPassword};Pooling=false;Timeout=5;Command Timeout=30");
    }

    private static string Text(int port) => port.ToString(CultureInfo.InvariantCulture);

    private static string Password() => RandomNumberGenerator.GetString(Alphabet, 32);

    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record Ports(int Postgres, int RabbitMq, int Management, int Api, int Worker);
}
