using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Resilience;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Resilience")]
public sealed class PostgresOutageE2ETests(E2EFixture stack)
{
    [E2EFact]
    public async Task StoppingThePostgresInTheMiddleOfABatch_Answers503_LosesNothingAndKeepsKeysFromDuplicating()
    {
        await PostgresOutageScenario.RunAsync(stack, compose => compose.StopAsync(ComposeControl.PostgresService));
    }

    [E2EFact]
    public async Task KillingThePostgresInTheMiddleOfABatch_KeepsEveryAcknowledgedWrite()
    {
        await PostgresOutageScenario.RunAsync(stack, compose => compose.KillAsync(ComposeControl.PostgresService));
    }

    [E2EFact]
    public async Task WithThePostgresStopped_ReadinessOfTheApiAndOfTheWorkerFail_AndBothRecoverWithoutRestarting()
    {
        using var http = stack.CreateClient();
        using var workerHttp = stack.Stack.CreateWorkerClient();
        var api = new E2EApi(http);
        var worker = new E2EApi(workerHttp);
        var compose = stack.Stack.Compose;
        var apiStartedAt = await compose.StartedAtAsync(ComposeControl.ApiService);
        var workerStartedAt = await compose.StartedAtAsync(ComposeControl.WorkerService);

        try
        {
            await compose.StopAsync(ComposeControl.PostgresService);

            await PostgresOutageScenario.AssertReadinessFailsAsync(api);
            await PostgresOutageScenario.AssertReadinessFailsAsync(worker);
        }
        finally
        {
            await compose.StartAsync(ComposeControl.PostgresService);
        }

        await compose.WaitHealthyAsync(ComposeControl.PostgresService, TimeSpan.FromSeconds(60));
        await PostgresOutageScenario.WaitReadyAsync(api, TimeSpan.FromSeconds(60));
        await PostgresOutageScenario.WaitReadyAsync(worker, TimeSpan.FromSeconds(60));

        (await compose.StartedAtAsync(ComposeControl.ApiService)).ShouldBe(apiStartedAt);
        (await compose.StartedAtAsync(ComposeControl.WorkerService)).ShouldBe(workerStartedAt);

        var accountId = await api.FundedAccountAsync("1.00");

        await PostgresOutageScenario.WaitReadsAsync(api, accountId, TimeSpan.FromSeconds(30));

        var balance = await api.BalanceAsync(accountId);

        balance.StatusCode.ShouldBe(200, balance.Body);
        balance.Text("balance").ShouldBe("1.00");
    }
}
