using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Entries;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Concurrency")]
public sealed class EntriesConcurrencyE2ETests(E2EFixture stack)
{
    private const int Debits = 10;

    [E2EFact]
    public async Task TenSimultaneousDebitsOfTwentyAgainstOneHundred_AcceptExactlyFive()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.FundedAccountAsync("100.00");

        var responses = await Task.WhenAll(
            Enumerable.Range(0, Debits).Select(_ => Task.Run(() => api.DebitAsync(accountId, "20.00"))));

        var accepted = responses.Where(response => response.StatusCode == 201).ToList();
        var refused = responses.Where(response => response.StatusCode == 422).ToList();

        accepted.Count.ShouldBe(5, string.Join(" | ", responses.Select(response => response.StatusCode)));
        refused.Count.ShouldBe(5);
        refused.ShouldAllBe(response => response.Code() == "INSUFFICIENT_FUNDS");
        accepted.Select(response => response.Text("balanceAfter")).Order().ShouldBe(
            ["0.00", "20.00", "40.00", "60.00", "80.00"]);

        var balance = await api.BalanceAsync(accountId);

        balance.Text("balance").ShouldBe("0.00");
    }

    [E2EFact]
    public async Task TwentySimultaneousReversalsOfTheSameEntry_AcceptOnlyOne()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.FundedAccountAsync("100.00");
        var debit = await api.DebitAsync(accountId, "30.00");

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => Task.Run(() => api.ReverseAsync(accountId, debit.Text("entryId")))));

        responses.Count(response => response.StatusCode == 201).ShouldBe(1);
        responses.Count(response => response.StatusCode == 409).ShouldBe(19);

        var balance = await api.BalanceAsync(accountId);

        balance.Text("balance").ShouldBe("100.00");
    }
}
