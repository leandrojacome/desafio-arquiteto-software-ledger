using System.Collections.Concurrent;
using System.Globalization;
using Ledger.EndToEnd.Tests.Support;
using Xunit.Abstractions;

namespace Ledger.EndToEnd.Tests.Events;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Latency")]
public sealed class EventLatencyE2ETests(E2EFixture stack, ITestOutputHelper output)
{
    private const int Entries = 1000;
    private const int Accounts = 20;
    private const int Writers = 4;
    private const double P99BudgetMilliseconds = 2000;
    private const double MaximumBudgetMilliseconds = 5000;

    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(90);

    [E2EFact]
    public async Task ThousandEntries_ReachTheQueueWithinTheP99AndTheMaximumOfTheEventBudget()
    {
        using var http = stack.CreateClient();
        using var broker = stack.Stack.RabbitMq();
        var api = new E2EApi(http);
        var queue = RabbitMqManagement.NewQueueName("event-latency");
        var committed = new ConcurrentDictionary<string, Committed>(StringComparer.Ordinal);

        await broker.DeclareBoundQueueAsync(queue);

        try
        {
            var accountIds = new List<string>(Accounts);

            for (var index = 0; index < Accounts; index++)
            {
                accountIds.Add(await api.CreateAccountAsync());
            }

            await using var recorder = new ArrivalRecorder(broker, queue);

            var perWriter = Entries / Writers;
            var writers = Enumerable.Range(0, Writers)
                .Select(writer => WriteAsync(api, accountIds.Where((_, index) => index % Writers == writer).ToList(), perWriter, committed))
                .ToArray();

            await Task.WhenAll(writers);

            await E2EWait.UntilAsync(
                () => Task.FromResult(committed.Keys.All(recorder.Arrivals.ContainsKey)),
                DeliveryTimeout,
                "Not every event of the thousand entries reached the test queue",
                TimeSpan.FromMilliseconds(200));

            committed.Count.ShouldBe(Entries);

            var offset = committed.Values.Min(entry => entry.ResponseAt - entry.RecordedAt);
            var latencies = committed
                .Select(pair => recorder.Arrivals[pair.Key] - pair.Value.RecordedAt - offset)
                .ToList();
            var samples = new LatencySamples(latencies);
            var factor = LatencySamples.Factor();
            var p99Budget = P99BudgetMilliseconds * factor;
            var maximumBudget = MaximumBudgetMilliseconds * factor;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"event delivery: p50 {samples.Percentile(0.50):0.0} ms, p99 {samples.Percentile(0.99):0.0} ms, max {samples.Percentile(1.0):0.0} ms, budget p99 {p99Budget:0} ms and max {maximumBudget:0} ms, factor {factor:0.##}, clock offset between the test and the stack {offset.TotalMilliseconds:0.0} ms. These budgets are hypotheses of the premises, not measurements of production."));

            samples.Percentile(0.99).ShouldBeLessThanOrEqualTo(p99Budget, "p99 of the event delivery");
            samples.Percentile(1.0).ShouldBeLessThanOrEqualTo(maximumBudget, "slowest event delivery");
        }
        finally
        {
            await broker.DeleteQueueAsync(queue);
        }
    }

    private static async Task WriteAsync(
        E2EApi api,
        List<string> accountIds,
        int entries,
        ConcurrentDictionary<string, Committed> committed)
    {
        for (var index = 0; index < entries; index++)
        {
            var accountId = accountIds[index % accountIds.Count];
            var response = await api.CreditAsync(accountId, "1.00");
            var receivedAt = TimeProvider.System.GetUtcNow();

            response.StatusCode.ShouldBe(201, response.Body);

            var recordedAt = DateTimeOffset.Parse(
                response.Text("recordedAt"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal);

            committed[response.Text("entryId")] = new Committed(recordedAt, receivedAt);
        }
    }

    private sealed record Committed(DateTimeOffset RecordedAt, DateTimeOffset ResponseAt);

    private sealed class ArrivalRecorder : IAsyncDisposable
    {
        private static readonly TimeSpan PollPause = TimeSpan.FromMilliseconds(50);

        private readonly RabbitMqManagement _broker;
        private readonly string _queue;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public ArrivalRecorder(RabbitMqManagement broker, string queue)
        {
            _broker = broker;
            _queue = queue;
            _loop = RunAsync();
        }

        public ConcurrentDictionary<string, DateTimeOffset> Arrivals { get; } = new(StringComparer.Ordinal);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _loop;
            _stop.Dispose();
        }

        private async Task RunAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                var batch = await _broker.ConsumeAllAsync(_queue);
                var arrivedAt = TimeProvider.System.GetUtcNow();

                foreach (var item in batch)
                {
                    Arrivals.TryAdd(item.EntryId, arrivedAt);
                }

                try
                {
                    await Task.Delay(PollPause, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
