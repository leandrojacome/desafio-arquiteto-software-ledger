using System.Globalization;

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

internal static class ConcurrencySettings
{
    private const string IterationsVariable = "CONCURRENCY_ITERATIONS";

    private const int DefaultIterations = 3;

    private static int Iterations
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(IterationsVariable);

            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : DefaultIterations;
        }
    }

    public static async Task RepeatAsync(Func<Task> scenario)
    {
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            await scenario();
        }
    }
}
