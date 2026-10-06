namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class ParallelGate
{
    private const int MinimumThreads = 256;

    static ParallelGate()
    {
        ThreadPool.SetMinThreads(MinimumThreads, MinimumThreads);
    }

    public static async Task<IReadOnlyList<T>> RunAsync<T>(int count, Func<int, Task<T>> action)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, count)
            .Select(async index =>
            {
                await gate.Task;

                return await action(index);
            })
            .ToArray();

        gate.SetResult();

        return await Task.WhenAll(tasks);
    }
}
