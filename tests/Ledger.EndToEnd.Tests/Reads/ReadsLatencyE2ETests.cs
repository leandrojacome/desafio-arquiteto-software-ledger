using Ledger.EndToEnd.Tests.Support;
using Xunit.Abstractions;

namespace Ledger.EndToEnd.Tests.Reads;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Latency")]
public sealed class ReadsLatencyE2ETests(E2EFixture stack, ITestOutputHelper output)
{
    private const int Entries = 500;
    private const int Queries = 300;
    private const int StatementLimit = 100;
    private const double BalanceBudgetMilliseconds = 50;
    private const double StatementBudgetMilliseconds = 200;

    [E2EFact]
    public async Task ThreeHundredBalanceQueriesAndThreeHundredStatementPages_StayInsideTheReadLatencyBudgets()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();
        var instants = new List<string>(Entries);

        for (var index = 0; index < Entries; index++)
        {
            var response = await api.CreditPacedAsync(accountId, "1.00");

            response.StatusCode.ShouldBe(201, response.Body);

            if (index % 50 == 0)
            {
                instants.Add(response.Text("recordedAt"));
            }
        }

        var warmupBalance = await api.BalanceAsync(accountId);
        var warmupStatement = await api.StatementAsync(accountId, $"limit={StatementLimit}");

        warmupBalance.StatusCode.ShouldBe(200, warmupBalance.Body);
        warmupStatement.StatusCode.ShouldBe(200, warmupStatement.Body);

        var balances = new List<TimeSpan>(Queries);
        var statements = new List<TimeSpan>(Queries);

        for (var index = 0; index < Queries; index++)
        {
            var asOf = index % 2 == 0 ? null : instants[index % instants.Count];
            var balance = await api.BalanceAsync(accountId, asOf);

            balance.StatusCode.ShouldBe(200, balance.Body);
            balances.Add(balance.Elapsed);
        }

        for (var index = 0; index < Queries; index++)
        {
            var statement = await api.StatementAsync(accountId, $"limit={StatementLimit}");

            statement.StatusCode.ShouldBe(200, statement.Body);
            statement.Json().GetProperty("items").GetArrayLength().ShouldBe(StatementLimit);
            statements.Add(statement.Elapsed);
        }

        var factor = LatencySamples.Factor();
        var balanceLatency = new LatencySamples(balances);
        var statementLatency = new LatencySamples(statements);

        output.WriteLine(balanceLatency.Describe("balance", BalanceBudgetMilliseconds * factor));
        output.WriteLine(statementLatency.Describe("statement", StatementBudgetMilliseconds * factor));

        balanceLatency.Percentile(0.99).ShouldBeLessThanOrEqualTo(
            BalanceBudgetMilliseconds * factor,
            balanceLatency.Describe("balance", BalanceBudgetMilliseconds * factor));
        statementLatency.Percentile(0.99).ShouldBeLessThanOrEqualTo(
            StatementBudgetMilliseconds * factor,
            statementLatency.Describe("statement", StatementBudgetMilliseconds * factor));
    }
}
