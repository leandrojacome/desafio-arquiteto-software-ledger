using System.Globalization;

namespace Ledger.Api.IntegrationTests.Infrastructure;

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
