using System.Globalization;

namespace Ledger.EndToEnd.Tests;

internal static class E2EEnvironment
{
    private const string BaseUrlVariable = "LEDGER_E2E_BASE_URL";

    private const string SkipVariable = "LEDGER_SKIP_E2E";

    private const string RequireVariable = "LEDGER_REQUIRE_DOCKER";

    private const string ProvisionVariable = "LEDGER_E2E_PROVISION";

    private const string WorkerUrlVariable = "LEDGER_E2E_WORKER_URL";

    private const string ManagementUrlVariable = "LEDGER_E2E_RABBITMQ_MANAGEMENT_URL";

    private const string EnabledValue = "true";

    private const string DefaultBaseUrl = "http://localhost:8080";

    private const string DefaultWorkerUrl = "http://localhost:8081";

    private const string DefaultManagementUrl = "http://localhost:15672";

    private const string ReadyPath = "/health/ready";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);

    public static string? SkipReason(Func<string, string?> variable)
    {
        if (IsOn(variable, RequireVariable) || !IsOn(variable, SkipVariable))
        {
            return null;
        }

        return $"{SkipVariable}={EnabledValue}: the end to end tests were skipped on purpose. Unset the variable and run them with 'LEDGER_E2E_PROVISION=true dotnet test tests/Ledger.EndToEnd.Tests', or bring the stack up with 'docker compose up -d --build --wait' first.";
    }

    public static bool ShouldProvision(Func<string, string?> variable) => IsOn(variable, ProvisionVariable);

    public static Uri ResolveBaseUrl(Func<string, string?> variable) =>
        ResolveUrl(variable, BaseUrlVariable, DefaultBaseUrl);

    public static Uri ResolveWorkerUrl(Func<string, string?> variable) =>
        ResolveUrl(variable, WorkerUrlVariable, DefaultWorkerUrl);

    public static Uri ResolveManagementUrl(Func<string, string?> variable) =>
        ResolveUrl(variable, ManagementUrlVariable, DefaultManagementUrl);

    public static Uri ResolveUrl(Func<string, string?> variable, string name, string fallback)
    {
        var value = variable(name);

        if (string.IsNullOrEmpty(value))
        {
            return new Uri(fallback);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
        {
            return url;
        }

        throw new InvalidOperationException(
            $"{name} is '{value}', which is not an absolute http or https address. Use something like {fallback}, or unset the variable to use that default.");
    }

    public static HttpClient CreateClient(Uri baseUrl)
    {
        return new HttpClient { BaseAddress = baseUrl, Timeout = RequestTimeout };
    }

    public static async Task WaitUntilReadyAsync(Uri baseUrl, CancellationToken cancellationToken)
    {
        using var client = CreateClient(baseUrl);

        var started = TimeProvider.System.GetTimestamp();
        var lastProblem = "no answer yet";

        while (TimeProvider.System.GetElapsedTime(started) < ReadyTimeout)
        {
            var problem = await ProbeAsync(client, cancellationToken);

            if (problem is null)
            {
                return;
            }

            lastProblem = problem;

            await Task.Delay(ProbeInterval, cancellationToken);
        }

        throw new InvalidOperationException(NotReadyMessage(baseUrl, lastProblem));
    }

    private static string NotReadyMessage(Uri baseUrl, string lastProblem)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"The stack did not answer 200 at {new Uri(baseUrl, ReadyPath)} within {ReadyTimeout.TotalSeconds:0} seconds ({lastProblem}). Bring it up with 'docker compose up -d --build --wait' and run the tests again, point {BaseUrlVariable} at a stack that is already running, or set {ProvisionVariable}={EnabledValue} so the tests bring up their own stack. To skip these tests on purpose, set {SkipVariable}={EnabledValue}.");
    }

    private static async Task<string?> ProbeAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probe.CancelAfter(ProbeTimeout);

        try
        {
            using var response = await client.GetAsync(ReadyPath, probe.Token);

            return response.IsSuccessStatusCode
                ? null
                : string.Create(CultureInfo.InvariantCulture, $"status {(int)response.StatusCode}");
        }
        catch (HttpRequestException exception)
        {
            return exception.Message;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "the readiness probe timed out";
        }
    }

    private static bool IsOn(Func<string, string?> variable, string name) =>
        string.Equals(variable(name), EnabledValue, StringComparison.OrdinalIgnoreCase);
}
