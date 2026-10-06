using System.Text.Json;

namespace Ledger.EndToEnd.Tests.Support;

internal static class PostgresOutageScenario
{
    private static readonly TimeSpan RecoverWithin = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan ReadinessWithin = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan RefusalWithin = TimeSpan.FromSeconds(5);

    public static async Task RunAsync(E2EFixture stack, Func<ComposeControl, Task> interrupt)
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var compose = stack.Stack.Compose;
        var accountId = await api.FundedAccountAsync("10.00");
        using var writer = new ContinuousWriter(api, accountId, 3, TimeSpan.FromMilliseconds(40));
        var apiStartedAt = await compose.StartedAtAsync(ComposeControl.ApiService);

        writer.Start();

        try
        {
            await E2EWait.UntilAsync(
                () => Task.FromResult(writer.Accepted >= 30),
                TimeSpan.FromSeconds(30),
                "The writers did not get 30 entries accepted before the interruption.");

            await interrupt(compose);

            await E2EWait.UntilAsync(
                () => Task.FromResult(writer.Refused >= 3),
                TimeSpan.FromSeconds(30),
                "The API never answered 503 while the database was down.");

            await AssertReadinessFailsAsync(api);
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
        }
        finally
        {
            await compose.StartAsync(ComposeControl.PostgresService);
        }

        await compose.WaitHealthyAsync(ComposeControl.PostgresService, RecoverWithin);
        await WaitReadyAsync(api, RecoverWithin);

        var acceptedAtRecovery = writer.Accepted;

        await E2EWait.UntilAsync(
            () => Task.FromResult(writer.Accepted >= acceptedAtRecovery + 10),
            TimeSpan.FromSeconds(30),
            "The writers did not get entries accepted again after the database came back.");
        await writer.StopAsync();

        await AssertNothingWasLostOrDuplicatedAsync(stack, api, writer, accountId);

        (await compose.StartedAtAsync(ComposeControl.ApiService)).ShouldBe(apiStartedAt, "the API must recover without a restart");
    }

    public static async Task AssertReadinessFailsAsync(E2EApi api)
    {
        E2EResponse? last = null;

        await E2EWait.UntilAsync(
            async () =>
            {
                last = await api.SendAsync(HttpMethod.Get, "/health/ready", anonymous: true);

                return last.StatusCode == 503;
            },
            ReadinessWithin,
            "/health/ready did not turn 503 with the database down");

        last.ShouldNotBeNull().Header("Retry-After").ShouldBe("5");
        (await api.SendAsync(HttpMethod.Get, "/health/live", anonymous: true)).StatusCode.ShouldBe(200);
    }

    public static async Task WaitReadyAsync(E2EApi api, TimeSpan timeout)
    {
        await E2EWait.UntilAsync(
            async () => (await api.SendAsync(HttpMethod.Get, "/health/ready", anonymous: true)).StatusCode == 200,
            timeout,
            "/health/ready did not return to 200 after the database came back",
            TimeSpan.FromSeconds(1));
    }

    public static async Task WaitReadsAsync(E2EApi api, string accountId, TimeSpan timeout)
    {
        await E2EWait.UntilAsync(
            async () => (await api.BalanceAsync(accountId)).StatusCode == 200 && (await api.StatementAsync(accountId)).StatusCode == 200,
            timeout,
            "The read routes did not return to 200 after the database came back",
            TimeSpan.FromSeconds(1));
    }

    private static async Task AssertNothingWasLostOrDuplicatedAsync(
        E2EFixture stack,
        E2EApi api,
        ContinuousWriter writer,
        string accountId)
    {
        var outcomes = writer.Outcomes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var unexpected = outcomes.Values.Where(outcome => outcome.Status is not (201 or 503)).ToList();

        unexpected.ShouldBeEmpty(
            $"only 201 and 503 are acceptable during an outage, saw: {string.Join(", ", unexpected.Select(item => $"{item.Status}/{item.Code}"))}");
        var refused = outcomes.Values.Where(outcome => outcome.Status == 503).ToList();

        refused.ShouldAllBe(outcome => outcome.RetryAfter == "1");
        refused.ShouldAllBe(outcome => outcome.Code == "SERVICE_UNAVAILABLE");
        refused.ShouldAllBe(outcome => outcome.Elapsed < RefusalWithin);

        foreach (var (key, outcome) in outcomes)
        {
            var repeated = await writer.RepeatUntilAcceptedAsync(key, TimeSpan.FromSeconds(60));

            if (outcome.Status == 201)
            {
                repeated.Header("Idempotent-Replayed").ShouldBe("true", $"the key {key} was accepted before and must be replayed");
            }
        }

        var database = stack.Stack.Database();
        var expectedEntries = outcomes.Count + 1;

        (await database.CountEntriesAsync(accountId)).ShouldBe(expectedEntries);
        (await database.InvariantViolationsAsync(accountId)).ShouldBeEmpty();

        await WaitReadsAsync(api, accountId, RecoverWithin);

        var balance = await api.BalanceAsync(accountId);

        balance.StatusCode.ShouldBe(200, balance.Body);
        balance.Text("balance").ShouldBe($"{10 + outcomes.Count}.00");

        var listed = await ListAllAsync(api, accountId);

        listed.Count.ShouldBe(expectedEntries);
        listed.Distinct().Count().ShouldBe(expectedEntries);
    }

    private static async Task<List<string>> ListAllAsync(E2EApi api, string accountId)
    {
        var ids = new List<string>();
        string? cursor = null;

        do
        {
            var suffix = cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}";
            var page = await api.StatementAsync(accountId, $"limit=200{suffix}");

            page.StatusCode.ShouldBe(200, page.Body);

            foreach (var item in page.Json().GetProperty("items").EnumerateArray())
            {
                ids.Add(item.GetProperty("entryId").GetString() ?? string.Empty);
            }

            var next = page.Json().GetProperty("nextCursor");
            cursor = next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        }
        while (cursor is not null);

        return ids;
    }
}
