using System.Globalization;

namespace Ledger.EndToEnd.Tests.Support;

internal sealed class ComposeControl(string project, IReadOnlyDictionary<string, string>? environment = null)
{
    public const string PostgresService = "postgres";

    public const string RabbitMqService = "rabbitmq";

    public const string WorkerService = "worker";

    public const string ApiService = "api";

    public const string MigratorService = "migrator";

    private const string ProjectVariable = "LEDGER_E2E_COMPOSE_PROJECT";

    private const string DefaultProject = "ledger-test";

    private static readonly string[] ComposeFiles = ["docker-compose.yml", "docker-compose.test.yml"];

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan UpTimeout = TimeSpan.FromMinutes(25);

    private readonly IReadOnlyDictionary<string, string> _environment =
        environment ?? new Dictionary<string, string>(StringComparer.Ordinal);

    public string Project { get; } = project;

    public static ComposeControl ForRunningStack()
    {
        var configured = Environment.GetEnvironmentVariable(ProjectVariable);

        return new ComposeControl(string.IsNullOrWhiteSpace(configured) ? DefaultProject : configured);
    }

    public async Task UpAsync()
    {
        await RunComposeAsync(UpTimeout, "up", "-d", "--build", "--wait", "--wait-timeout", "900");
    }

    public async Task DownAsync()
    {
        await RunComposeAsync(CommandTimeout, "--profile", "*", "down", "--volumes", "--remove-orphans");
    }

    public async Task StopAsync(string service)
    {
        await RunComposeAsync(CommandTimeout, "stop", service);
    }

    public async Task StartAsync(string service)
    {
        await RunComposeAsync(CommandTimeout, "start", service);
    }

    public async Task KillAsync(string service)
    {
        await RunComposeAsync(CommandTimeout, "kill", service);
    }

    public async Task RestartAsync(string service)
    {
        await RunComposeAsync(CommandTimeout, "restart", service);
    }

    public async Task<int> ExitCodeOfAsync(string service)
    {
        var container = await ContainerOfAsync(service);
        var output = await RunDockerAsync("inspect", "--format", "{{.State.ExitCode}}", container);

        return int.Parse(output.Trim(), CultureInfo.InvariantCulture);
    }

    public async Task<string> StatusOfAsync(string service)
    {
        var container = await ContainerOfAsync(service);
        var output = await RunDockerAsync(
            "inspect",
            "--format",
            "{{.State.Status}}/{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}",
            container);

        return output.Trim();
    }

    public async Task<string> StartedAtAsync(string service)
    {
        var container = await ContainerOfAsync(service);
        var output = await RunDockerAsync("inspect", "--format", "{{.State.StartedAt}}", container);

        return output.Trim();
    }

    public async Task<string> LogsAsync(string service)
    {
        var result = await RunComposeResultAsync(CommandTimeout, "logs", "--no-color", service);

        return result.Output + result.Error;
    }

    public async Task WaitHealthyAsync(string service, TimeSpan timeout)
    {
        await E2EWait.UntilAsync(
            async () => (await StatusOfAsync(service)).EndsWith("/healthy", StringComparison.Ordinal),
            timeout,
            $"The compose service '{service}' did not become healthy.");
    }

    private async Task<string> ContainerOfAsync(string service)
    {
        var output = await RunComposeAsync(CommandTimeout, "ps", "-a", "-q", service);
        var identifiers = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return identifiers.Length == 1
            ? identifiers[0]
            : throw new InvalidOperationException(
                $"Expected one container of the compose service '{service}' in the project '{Project}' and found {identifiers.Length}. Bring the stack up with 'docker compose -p ledger-test -f docker-compose.yml -f docker-compose.test.yml up -d --build --wait', set LEDGER_E2E_PROVISION=true so the tests bring up their own stack, or set {ProjectVariable} to the compose project name of the stack under test.");
    }

    private async Task<string> RunComposeAsync(TimeSpan timeout, params string[] arguments)
    {
        var result = await RunComposeResultAsync(timeout, arguments);

        return result.ExitCode == 0
            ? result.Output
            : throw new InvalidOperationException(
                $"'docker compose {string.Join(' ', arguments)}' failed with exit code {result.ExitCode} in the project '{Project}': {result.Error}");
    }

    private Task<ProcessResult> RunComposeResultAsync(TimeSpan timeout, params string[] arguments)
    {
        var all = new List<string> { "compose", "-p", Project };

        foreach (var file in ComposeFiles)
        {
            all.Add("-f");
            all.Add(file);
        }

        all.AddRange(arguments);

        return ProcessRunner.RunAsync("docker", all, E2EPaths.RepositoryRoot(), _environment, timeout);
    }

    private async Task<string> RunDockerAsync(params string[] arguments)
    {
        var result = await ProcessRunner.RunAsync("docker", arguments, E2EPaths.RepositoryRoot(), _environment, CommandTimeout);

        return result.ExitCode == 0
            ? result.Output
            : throw new InvalidOperationException(
                $"'docker {string.Join(' ', arguments)}' failed with exit code {result.ExitCode}: {result.Error}");
    }
}
