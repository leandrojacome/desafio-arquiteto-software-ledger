using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Persistence.Support;

internal static class TimePump
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(50);

    public static async Task<TResult> RunAsync<TResult>(FakeTimeProvider time, Task<TResult> work)
    {
        while (!work.IsCompleted)
        {
            time.Advance(Step);

            await Task.WhenAny(work, Task.Delay(TimeSpan.FromMilliseconds(2)));
        }

        return await work;
    }
}
