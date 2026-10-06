using System.Diagnostics;
using System.Globalization;
using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Latency")]
public sealed class ReadLatencyTests(PostgresFixture postgres, ITestOutputHelper output) : IAsyncLifetime
{
    private const int Entries = 10_000;
    private const int Samples = 200;
    private static readonly TimeSpan GrossLimit = TimeSpan.FromSeconds(2);

    private ReadWorld _world = null!;

    public Task InitializeAsync()
    {
        _world = ReadWorld.Create(postgres);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [DockerFact]
    public async Task Reads_OfAnAccountWithTenThousandEntries_AnswerFastAndReportTheirPercentiles()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        var start = ReadClock.UtcNow.AddDays(-30);
        await _world.Ledger.SeedChainAsync(accountId, Entries, start, TimeSpan.FromSeconds(1));

        var middle = StatementItem.FormatInstant(start.AddSeconds(Entries / 2));
        var currentBalance = new List<TimeSpan>();
        var historicalBalance = new List<TimeSpan>();
        var firstPage = new List<TimeSpan>();
        var deepPage = new List<TimeSpan>();

        for (var sample = 0; sample < Samples; sample++)
        {
            currentBalance.Add(await MeasureAsync(() => _world.Client.BalanceAsync(accountId)));
            historicalBalance.Add(await MeasureAsync(() => _world.Client.BalanceAsOfAsync(accountId, middle)));
            firstPage.Add(await MeasureAsync(() => _world.Client.StatementAsync(accountId, "limit=100")));
            deepPage.Add(await MeasureAsync(
                () => _world.Client.StatementAsync(accountId, $"limit=100&to={Uri.EscapeDataString(middle)}")));
        }

        Report("balance current", currentBalance);
        Report("balance as of the middle", historicalBalance);
        Report("statement first page of 100", firstPage);
        Report("statement page of 100 from the middle", deepPage);

        foreach (var group in new[] { currentBalance, historicalBalance, firstPage, deepPage })
        {
            Percentile(group, 0.99).ShouldBeLessThan(GrossLimit);
        }
    }

    private static async Task<TimeSpan> MeasureAsync(Func<Task<ReadResponse>> request)
    {
        var started = Stopwatch.GetTimestamp();
        using var response = await request();
        var elapsed = Stopwatch.GetElapsedTime(started);

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);

        return elapsed;
    }

    private static TimeSpan Percentile(List<TimeSpan> samples, double fraction)
    {
        var ordered = samples.Order().ToList();
        var index = (int)Math.Ceiling(fraction * ordered.Count) - 1;

        return ordered[Math.Clamp(index, 0, ordered.Count - 1)];
    }

    private void Report(string name, List<TimeSpan> samples)
    {
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: p50 {Percentile(samples, 0.50).TotalMilliseconds:F1} ms, p99 {Percentile(samples, 0.99).TotalMilliseconds:F1} ms over {samples.Count} requests (informative, hypotheses of the premises: 50 ms for balance and 200 ms for statement)"));
    }
}
