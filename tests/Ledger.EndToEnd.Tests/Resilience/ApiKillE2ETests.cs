using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Resilience;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Resilience")]
public sealed class ApiKillE2ETests(E2EFixture stack)
{
    private static readonly TimeSpan RecoverWithin = TimeSpan.FromSeconds(90);

    [E2EFact]
    public async Task KillingTheApiInTheMiddleOfTheLoad_LosesNothingAcknowledged_AndRepeatingTheKeysDuplicatesNothing()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var compose = stack.Stack.Compose;
        var accountId = await api.FundedAccountAsync("10.00");
        using var writer = new ContinuousWriter(api, accountId, 3, TimeSpan.FromMilliseconds(100));

        writer.Start();

        try
        {
            await E2EWait.UntilAsync(
                () => Task.FromResult(writer.Accepted >= 30),
                TimeSpan.FromSeconds(30),
                "The writers did not get 30 entries accepted before the API was killed.");

            await compose.KillAsync(ComposeControl.ApiService);

            await E2EWait.UntilAsync(
                () => Task.FromResult(writer.Outcomes.Values.Count(outcome => outcome.Status <= 0) >= 3),
                TimeSpan.FromSeconds(30),
                "The writers never noticed that the API was gone.");
        }
        finally
        {
            await compose.StartAsync(ComposeControl.ApiService);
        }

        await compose.WaitHealthyAsync(ComposeControl.ApiService, RecoverWithin);
        await PostgresOutageScenario.WaitReadyAsync(api, RecoverWithin);

        var acceptedAtRecovery = writer.Accepted;

        await E2EWait.UntilAsync(
            () => Task.FromResult(writer.Accepted >= acceptedAtRecovery + 10),
            TimeSpan.FromSeconds(30),
            "The writers did not get entries accepted again after the API came back.");
        await writer.StopAsync();

        var outcomes = writer.Outcomes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var unexpected = outcomes.Values.Where(outcome => outcome.Status is not (201 or 0 or -1)).ToList();

        unexpected.ShouldBeEmpty(
            $"only 201 and a lost connection are acceptable when the API dies, saw: {string.Join(", ", unexpected.Select(item => $"{item.Status}/{item.Code}"))}");
        outcomes.Values.Count(outcome => outcome.Status <= 0).ShouldBeGreaterThanOrEqualTo(1);

        foreach (var (key, outcome) in outcomes)
        {
            var repeated = await writer.RepeatUntilAcceptedAsync(key, TimeSpan.FromSeconds(60));

            if (outcome.Status == 201)
            {
                repeated.Header("Idempotent-Replayed").ShouldBe("true", $"the key {key} was acknowledged before the kill and must be replayed");
            }
        }

        var database = stack.Stack.Database();
        var expectedEntries = outcomes.Count + 1;

        (await database.CountEntriesAsync(accountId)).ShouldBe(expectedEntries);
        (await database.InvariantViolationsAsync(accountId)).ShouldBeEmpty();

        await PostgresOutageScenario.WaitReadsAsync(api, accountId, RecoverWithin);

        var balance = await api.BalanceAsync(accountId);

        balance.StatusCode.ShouldBe(200, balance.Body);
        balance.Text("balance").ShouldBe($"{10 + outcomes.Count}.00");
    }
}
