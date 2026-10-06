using Ledger.EndToEnd.Tests.Support;
using Xunit.Abstractions;

namespace Ledger.EndToEnd.Tests.Entries;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Latency")]
public sealed class EntriesLatencyE2ETests(E2EFixture stack, ITestOutputHelper output)
{
    private const int Credits = 200;
    private const int Accounts = 4;
    private const double BudgetMilliseconds = 150;

    [E2EFact]
    public async Task TwoHundredSequentialCredits_StayInsideTheWriteLatencyBudget()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountIds = new List<string>(Accounts);
        var samples = new List<TimeSpan>(Credits);

        for (var index = 0; index < Accounts; index++)
        {
            accountIds.Add(await api.FundedAccountAsync("1.00"));
        }

        for (var index = 0; index < Credits; index++)
        {
            var response = await api.CreditAsync(accountIds[index % Accounts], "1.00");

            response.StatusCode.ShouldBe(201, response.Body);
            samples.Add(response.Elapsed);
        }

        var latency = new LatencySamples(samples);
        var budget = BudgetMilliseconds * LatencySamples.Factor();

        output.WriteLine(latency.Describe("write", budget));
        latency.Percentile(0.99).ShouldBeLessThanOrEqualTo(budget, latency.Describe("write", budget));
    }
}
