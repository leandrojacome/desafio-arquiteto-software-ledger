using System.Diagnostics;

namespace Ledger.EndToEnd.Tests.Support;

internal static class E2EWait
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string failure, TimeSpan? interval = null)
    {
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            if (await condition())
            {
                return;
            }

            if (Stopwatch.GetElapsedTime(started) >= timeout)
            {
                throw new Xunit.Sdk.XunitException($"{failure} (waited {timeout.TotalSeconds:0} seconds)");
            }

            await Task.Delay(interval ?? DefaultInterval, CancellationToken.None);
        }
    }
}
