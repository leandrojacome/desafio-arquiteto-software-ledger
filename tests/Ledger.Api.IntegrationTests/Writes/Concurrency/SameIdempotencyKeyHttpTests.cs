using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class SameIdempotencyKeyHttpTests(PostgresFixture postgres)
{
    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task FortyIdenticalCallsWithTheSameKey_RegisterOneEntryAndAllAnswerTheSameBody()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await client.CreateFundedAccountAsync("1000.00");
            var key = WriteClient.NewKey();

            var responses = await ParallelGate.RunAsync(40, _ => client.DebitAsync(accountId, "80.00", key));

            responses.ShouldAllBe(response => response.StatusCode == 201);
            responses.Select(response => response.Body).Distinct().Count().ShouldBe(1);
            responses.Select(response => response.Text("entryId")).Distinct().Count().ShouldBe(1);
            responses.Count(response => response.Header("Idempotent-Replayed") == "true").ShouldBe(39);
            responses.Count(response => !response.HasHeader("Idempotent-Replayed")).ShouldBe(1);
            (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
            (await _data.CountOutboxAsync(accountId)).ShouldBe(2);
            (await _data.CountKeysAsync(accountId)).ShouldBe(2);
            (await _data.BalanceAsync(accountId)).ShouldBe(920.00m);
            await _data.AssertConsistentAsync(accountId);
        });
    }

    [DockerFact]
    public async Task TwentyCallsWithTheSameKeyAndDifferentAmounts_AcceptOneAndRefuseTheOthersAsReused()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await client.CreateFundedAccountAsync("1000.00");
            var key = WriteClient.NewKey();

            var responses = await ParallelGate.RunAsync(
                20,
                index => client.DebitAsync(accountId, $"{index + 1}.00", key));

            responses.Count(response => response.StatusCode == 201).ShouldBe(1);
            responses.Count(response => response.StatusCode == 422).ShouldBe(19);
            responses.Where(response => response.StatusCode == 422)
                .ShouldAllBe(response => response.Text("code") == "IDEMPOTENCY_KEY_REUSED");
            (await _data.CountEntriesAsync(accountId)).ShouldBe(2);

            var accepted = responses.Single(response => response.StatusCode == 201);

            (await _data.BalanceAsync(accountId)).ShouldBe(decimal.Parse(accepted.Text("balanceAfter"), System.Globalization.CultureInfo.InvariantCulture));
            await _data.AssertConsistentAsync(accountId);
        });
    }

    [DockerFact]
    public async Task TheSameKeyFiredOnTwoAccountsAtOnce_RegistersOneEntryOnEach()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var first = await client.CreateFundedAccountAsync("100.00");
        var second = await client.CreateFundedAccountAsync("100.00");
        var key = WriteClient.NewKey();

        var responses = await ParallelGate.RunAsync(
            20,
            index => client.DebitAsync(index % 2 == 0 ? first : second, "10.00", key));

        responses.ShouldAllBe(response => response.StatusCode == 201);
        (await _data.CountEntriesAsync(first)).ShouldBe(2);
        (await _data.CountEntriesAsync(second)).ShouldBe(2);
        (await _data.BalanceAsync(first)).ShouldBe(90.00m);
        (await _data.BalanceAsync(second)).ShouldBe(90.00m);
        responses.Select(response => response.Text("entryId")).Distinct().Count().ShouldBe(2);
    }

    [DockerFact]
    public async Task ManyKeysEachRepeatedConcurrently_RegisterEachEntryExactlyOnce()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("1000.00");
        var keys = Enumerable.Range(0, 10).Select(_ => WriteClient.NewKey()).ToList();

        var responses = await ParallelGate.RunAsync(50, index => client.DebitAsync(accountId, "10.00", keys[index % keys.Count]));

        responses.ShouldAllBe(response => response.StatusCode == 201);
        responses.Select(response => response.Text("entryId")).Distinct().Count().ShouldBe(10);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(11);
        (await _data.BalanceAsync(accountId)).ShouldBe(900.00m);
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task ARefusedDebitRepeatedConcurrently_StaysRefusedAndConsumesNothing()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("10.00");
        var key = WriteClient.NewKey();

        var responses = await ParallelGate.RunAsync(20, _ => client.DebitAsync(accountId, "50.00", key));

        responses.ShouldAllBe(response => response.StatusCode == 422);
        (await _data.CountKeysAsync(accountId)).ShouldBe(1);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(1);
        (await client.CreditAsync(accountId, "100.00")).StatusCode.ShouldBe(201);

        var accepted = await client.DebitAsync(accountId, "50.00", key);

        accepted.StatusCode.ShouldBe(201, accepted.Body);
        accepted.HasHeader("Idempotent-Replayed").ShouldBeFalse();
    }
}
