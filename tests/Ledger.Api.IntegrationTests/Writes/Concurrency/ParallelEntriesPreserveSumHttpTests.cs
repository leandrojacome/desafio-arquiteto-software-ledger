using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class ParallelEntriesPreserveSumHttpTests(PostgresFixture postgres)
{
    private const int Seed = 20_261_001;

    private readonly WriteTestData _data = new(postgres);

    [DockerTheory]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(256)]
    [SuppressMessage("Security", "CA5394",
        Justification = "A fixed seed makes the amounts of the scenario reproducible. No secret is involved.")]
    public async Task ConcurrentCreditsAndDebits_ChangeTheBalanceByTheSignedSumOfTheAcceptedOnes(int calls)
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await client.CreateFundedAccountAsync("100000.00");
            var plan = Enumerable.Range(0, calls)
                .Select(index =>
                {
                    var random = new Random(Seed + index);
                    var cents = random.Next(1, 50_001);

                    return (IsCredit: random.Next(2) == 0, Amount: cents / 100m);
                })
                .ToList();

            var responses = await ParallelGate.RunAsync(
                calls,
                index => plan[index].IsCredit
                    ? client.CreditAsync(accountId, plan[index].Amount.ToString("F2", CultureInfo.InvariantCulture))
                    : client.DebitAsync(accountId, plan[index].Amount.ToString("F2", CultureInfo.InvariantCulture)));

            responses.ShouldAllBe(response => response.StatusCode < 500);

            var accepted = responses
                .Select((response, index) => (Response: response, Plan: plan[index]))
                .Where(item => item.Response.StatusCode == 201)
                .ToList();
            var signedSum = accepted.Sum(item => item.Plan.IsCredit ? item.Plan.Amount : -item.Plan.Amount);

            (await _data.CountEntriesAsync(accountId)).ShouldBe(accepted.Count + 1);
            (await _data.BalanceAsync(accountId)).ShouldBe(100000.00m + signedSum);
            accepted.Select(item => item.Response.Json().GetProperty("accountVersion").GetInt64())
                .Order()
                .ShouldBe(Enumerable.Range(2, accepted.Count).Select(version => (long)version));
            await _data.AssertConsistentAsync(accountId);
        });
    }

    [DockerFact]
    public async Task Debits_ThatDoNotDivideTheBalance_AcceptOnlyWholeDebitsAndLeaveTheRemainder()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("1000.00");

        var responses = await ParallelGate.RunAsync(100, _ => client.DebitAsync(accountId, "30.00"));

        responses.Count(response => response.StatusCode == 201).ShouldBe(33);
        responses.Count(response => response.StatusCode == 422).ShouldBe(67);
        (await _data.BalanceAsync(accountId)).ShouldBe(10.00m);
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task Debits_WithAnOverdraftLimit_EndExactlyAtMinusTheLimit()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("1000.00", overdraftLimit: "500.00");

        var responses = await ParallelGate.RunAsync(100, _ => client.DebitAsync(accountId, "30.00"));

        responses.Count(response => response.StatusCode == 201).ShouldBe(50);
        responses.Count(response => response.StatusCode == 422).ShouldBe(50);
        (await _data.BalanceAsync(accountId)).ShouldBe(-500.00m);
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task ManyAccountsFiredTogether_EndConsistent()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var accounts = new List<string>();

        for (var index = 0; index < 50; index++)
        {
            accounts.Add(await client.CreateFundedAccountAsync("1000.00"));
        }

        var responses = await ParallelGate.RunAsync(
            accounts.Count * 20,
            index => index % 3 == 0
                ? client.CreditAsync(accounts[index % accounts.Count], "7.00")
                : client.DebitAsync(accounts[index % accounts.Count], "5.00"));

        responses.ShouldAllBe(response => response.StatusCode == 201);

        foreach (var accountId in accounts)
        {
            await _data.AssertConsistentAsync(accountId);
        }
    }
}
