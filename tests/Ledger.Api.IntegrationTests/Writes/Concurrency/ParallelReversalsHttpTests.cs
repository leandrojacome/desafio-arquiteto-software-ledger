using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class ParallelReversalsHttpTests(PostgresFixture postgres)
{
    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task ReversalsOfTheSameEntry_YieldOneCreatedAndTheRestConflict()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await client.CreateFundedAccountAsync("1000.00");
            var original = await client.DebitAsync(accountId, "150.00");

            var responses = await ParallelGate.RunAsync(
                20,
                _ => client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey()));

            responses.Count(response => response.StatusCode == 201).ShouldBe(1);
            responses.Count(response => response.StatusCode == 409).ShouldBe(19);
            responses.Where(response => response.StatusCode == 409)
                .ShouldAllBe(response => response.Text("code") == "ENTRY_ALREADY_REVERSED");
            (await _data.BalanceAsync(accountId)).ShouldBe(1000.00m);
            (await _data.CountEntriesAsync(accountId)).ShouldBe(3);
            (await _data.CountOutboxAsync(accountId)).ShouldBe(3);
            (await _data.CountKeysAsync(accountId)).ShouldBe(3);
            await _data.AssertConsistentAsync(accountId);
        });
    }

    [DockerFact]
    public async Task CompetingReversals_WhenTheBalanceCoversOnlyOne_YieldOneCreatedAndOneConflict()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await client.CreateAccountAsync();
            var credit = await client.CreditAsync(accountId, "100.00");

            var responses = await ParallelGate.RunAsync(
                2,
                _ => client.PostReversalAsync(accountId, credit.Text("entryId"), WriteClient.NewKey()));

            responses.Select(response => response.StatusCode).Order().ShouldBe([201, 409]);
            responses.Single(response => response.StatusCode == 409).Text("code").ShouldBe("ENTRY_ALREADY_REVERSED");
            (await _data.BalanceAsync(accountId)).ShouldBe(0m);
            (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
            await _data.AssertConsistentAsync(accountId);
        });
    }

    [DockerFact]
    public async Task ReversalsOfDifferentCreditsWhenTheBalanceCoversOnlyOne_YieldOneCreatedAndOneRefusal()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await client.CreateAccountAsync();
            var first = await client.CreditAsync(accountId, "100.00");
            var second = await client.CreditAsync(accountId, "100.00");

            (await client.DebitAsync(accountId, "100.00")).StatusCode.ShouldBe(201);

            var responses = await ParallelGate.RunAsync(
                2,
                index => client.PostReversalAsync(
                    accountId,
                    index == 0 ? first.Text("entryId") : second.Text("entryId"),
                    WriteClient.NewKey()));

            responses.Select(response => response.StatusCode).Order().ShouldBe([201, 422]);
            responses.Single(response => response.StatusCode == 422).Text("code").ShouldBe("INSUFFICIENT_FUNDS");
            (await _data.BalanceAsync(accountId)).ShouldBe(0m);
            await _data.AssertConsistentAsync(accountId);
        });
    }

    [DockerFact]
    public async Task TheSameReversalKeyRepeatedConcurrently_CreatesOneReversalAndReplaysTheRest()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("1000.00");
        var original = await client.DebitAsync(accountId, "150.00");
        var key = WriteClient.NewKey();

        var responses = await ParallelGate.RunAsync(
            20,
            _ => client.PostReversalAsync(accountId, original.Text("entryId"), key));

        responses.ShouldAllBe(response => response.StatusCode == 201);
        responses.Select(response => response.Text("entryId")).Distinct().Count().ShouldBe(1);
        responses.Count(response => response.Header("Idempotent-Replayed") == "true").ShouldBe(19);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(3);
        (await _data.BalanceAsync(accountId)).ShouldBe(1000.00m);
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task ReversalsAndNewDebitsTogether_NeverBreakTheBooks()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("1000.00");
        var originals = new List<string>();

        for (var index = 0; index < 10; index++)
        {
            originals.Add((await client.DebitAsync(accountId, "10.00")).Text("entryId"));
        }

        var responses = await ParallelGate.RunAsync(
            40,
            index => index % 2 == 0
                ? client.PostReversalAsync(accountId, originals[(index / 2) % originals.Count], WriteClient.NewKey())
                : client.DebitAsync(accountId, "1.00"));

        responses.Select(response => response.StatusCode).ShouldAllBe(status => status == 201 || status == 409);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(1 + 10 + responses.Count(response => response.StatusCode == 201));
        await _data.AssertConsistentAsync(accountId);
    }
}
