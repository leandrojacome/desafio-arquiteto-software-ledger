using System.Diagnostics;
using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Reads;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Resilience")]
public sealed class ReadsResilienceE2ETests(E2EFixture stack)
{
    private const string Database = ComposeControl.PostgresService;
    private static readonly TimeSpan AnswerWithin = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RecoverWithin = TimeSpan.FromSeconds(30);

    [E2EFact]
    public async Task WithTheDatabaseStopped_TheReadRoutesAnswer503FastAndRecoverWithoutRestartingTheApi()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.FundedAccountAsync("10.00");

        try
        {
            await stack.Stack.Compose.StopAsync(Database);

            var balance = await api.BalanceAsync(accountId);
            var statement = await api.StatementAsync(accountId);
            var live = await api.SendAsync(HttpMethod.Get, "/health/live", anonymous: true);

            balance.StatusCode.ShouldBe(503, balance.Body);
            balance.Header("Retry-After").ShouldNotBeNullOrWhiteSpace();
            balance.Elapsed.ShouldBeLessThan(AnswerWithin);
            statement.StatusCode.ShouldBe(503, statement.Body);
            statement.Header("Retry-After").ShouldNotBeNullOrWhiteSpace();
            statement.Elapsed.ShouldBeLessThan(AnswerWithin);
            live.StatusCode.ShouldBe(200, live.Body);
        }
        finally
        {
            await stack.Stack.Compose.StartAsync(Database);
        }

        await WaitForBothRoutesAsync(api, accountId);
    }

    private static async Task WaitForBothRoutesAsync(E2EApi api, string accountId)
    {
        var started = Stopwatch.GetTimestamp();
        var lastBalance = 0;
        var lastStatement = 0;

        while (Stopwatch.GetElapsedTime(started) < RecoverWithin)
        {
            lastBalance = (await api.BalanceAsync(accountId)).StatusCode;
            lastStatement = (await api.StatementAsync(accountId)).StatusCode;

            if (lastBalance == 200 && lastStatement == 200)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
        }

        throw new Xunit.Sdk.XunitException(
            $"The read routes did not return to 200 within {RecoverWithin.TotalSeconds:0} seconds (balance {lastBalance}, statement {lastStatement}).");
    }
}
