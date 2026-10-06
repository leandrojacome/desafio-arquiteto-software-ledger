using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests;

public sealed class E2EFixture : IAsyncLifetime
{
    private E2EStack? _stack;

    public Uri BaseUrl => Stack.ApiUrl;

    internal E2EStack Stack => _stack ?? throw new InvalidOperationException("The end to end stack was not prepared.");

    public async Task InitializeAsync()
    {
        if (E2EEnvironment.SkipReason(Environment.GetEnvironmentVariable) is not null)
        {
            return;
        }

        _stack = E2EEnvironment.ShouldProvision(Environment.GetEnvironmentVariable)
            ? await E2EProvisioner.UpAsync()
            : E2EStack.Attach(Environment.GetEnvironmentVariable);

        await E2EEnvironment.WaitUntilReadyAsync(Stack.ApiUrl, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_stack is { Provisioned: true } provisioned)
        {
            await provisioned.Compose.DownAsync();
        }
    }

    internal HttpClient CreateClient() => E2EEnvironment.CreateClient(Stack.ApiUrl);
}
