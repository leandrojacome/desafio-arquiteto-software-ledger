namespace Ledger.Infrastructure.Tests.Security;

internal static class EventuallyTrue
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    public static async Task WaitAsync(Func<bool> condition, string because)
    {
        using var timeout = new CancellationTokenSource(DefaultTimeout);

        while (!condition())
        {
            try
            {
                await Task.Delay(PollInterval, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"The condition was not met in time: {because}");
            }
        }
    }
}
