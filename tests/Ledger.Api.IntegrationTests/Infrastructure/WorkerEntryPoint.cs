extern alias LedgerWorker;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class WorkerEntryPoint
{
    public static async Task<int> RunAsync(params string[] arguments)
    {
        var entryPoint = typeof(LedgerWorker::Program).Assembly.EntryPoint ??
                         throw new InvalidOperationException("The worker assembly has no entry point.");

        var invoked = entryPoint.Invoke(null, [arguments]);

        return invoked switch
        {
            Task<int> task => await task,
            int code => code,
            _ => throw new InvalidOperationException("The worker entry point returned an unexpected value.")
        };
    }
}
