using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class ReverseEntryTests(PostgresFixture postgres)
{
    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task ReversalOfADebit_CreditsTheSameAmountAndPointsToTheOriginal()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("1000.00");
        var original = await client.DebitAsync(accountId, "150.00");

        var reversal = await client.PostReversalAsync(
            accountId,
            original.Text("entryId"),
            WriteClient.NewKey(),
            "{\"description\":\"Cobrança em duplicidade confirmada pela conciliação\"}");

        reversal.StatusCode.ShouldBe(201, reversal.Body);
        reversal.Header("Cache-Control").ShouldBe("no-store");
        reversal.Header("Location").ShouldBe($"/v1/accounts/{accountId}/entries");
        reversal.Text("type").ShouldBe("CREDIT");
        reversal.Text("amount").ShouldBe("150.00");
        reversal.Text("currency").ShouldBe("BRL");
        reversal.Text("balanceAfter").ShouldBe("1000.00");
        reversal.Text("reversesEntryId").ShouldBe(original.Text("entryId"));
        reversal.Text("occurredAt").ShouldBe(reversal.Text("recordedAt"));
        reversal.Text("description").ShouldBe("Cobrança em duplicidade confirmada pela conciliação");
        reversal.Json().GetProperty("reference").ValueKind.ShouldBe(JsonValueKind.Null);
        reversal.Json().GetProperty("accountVersion").GetInt64().ShouldBe(3);
        reversal.Text("entryId").ShouldNotBe(original.Text("entryId"));
        (await _data.BalanceAsync(accountId)).ShouldBe(1000.00m);
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task ReversalOfADebit_LeavesTheOriginalEntryUntouched()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("1000.00");
        var original = await client.DebitAsync(accountId, "150.00");
        var before = await _data.Queries.EntriesAsync(WriteTestData.Account(accountId), CancellationToken.None);

        (await client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey())).StatusCode.ShouldBe(201);

        var after = await _data.Queries.EntriesAsync(WriteTestData.Account(accountId), CancellationToken.None);

        after.Count.ShouldBe(before.Count + 1);
        after.Take(before.Count).ShouldBe(before);
        after[^1].ReversesEntryId.ShouldBe(Guid.Parse(original.Text("entryId")));
    }

    [DockerFact]
    public async Task ReversalOfACredit_DebitsTheAmountWhileTheFundsAreStillThere()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var credit = await client.CreditAsync(accountId, "40.00");

        var reversal = await client.PostReversalAsync(accountId, credit.Text("entryId"), WriteClient.NewKey());

        reversal.StatusCode.ShouldBe(201, reversal.Body);
        reversal.Text("type").ShouldBe("DEBIT");
        reversal.Text("amount").ShouldBe("40.00");
        reversal.Text("balanceAfter").ShouldBe("100.00");
    }

    [DockerFact]
    public async Task ReversalOfASpentCredit_IsRefusedAndTheKeyStaysFree()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        var credit = await client.CreditAsync(accountId, "100.00");

        (await client.DebitAsync(accountId, "100.00")).StatusCode.ShouldBe(201);

        var key = WriteClient.NewKey();
        var refused = await client.PostReversalAsync(accountId, credit.Text("entryId"), key);

        refused.ShouldBeProblem(422, "INSUFFICIENT_FUNDS");
        refused.Body.ShouldNotContain("100.00");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);

        (await client.CreditAsync(accountId, "100.00")).StatusCode.ShouldBe(201);

        var accepted = await client.PostReversalAsync(accountId, credit.Text("entryId"), key);

        accepted.StatusCode.ShouldBe(201, accepted.Body);
        accepted.HasHeader("Idempotent-Replayed").ShouldBeFalse();
        accepted.Text("balanceAfter").ShouldBe("0.00");
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task ReversalOfAnEntryOfAnotherAccountOrOfNone_IsTheSameNotFound()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var mine = await client.CreateFundedAccountAsync("100.00");
        var theirs = await client.CreateFundedAccountAsync("100.00");
        var foreign = await client.DebitAsync(theirs, "10.00");

        var ofAnotherAccount = await client.PostReversalAsync(mine, foreign.Text("entryId"), WriteClient.NewKey());
        var ofNone = await client.PostReversalAsync(mine, Guid.NewGuid().ToString("D"), WriteClient.NewKey());
        var notAGuid = await client.PostReversalAsync(mine, "nope", WriteClient.NewKey());

        var first = ofAnotherAccount.ShouldBeProblem(404, "ENTRY_NOT_FOUND");
        var second = ofNone.ShouldBeProblem(404, "ENTRY_NOT_FOUND");
        notAGuid.ShouldBeProblem(404, "ENTRY_NOT_FOUND");
        first.GetProperty("detail").GetString().ShouldBe(second.GetProperty("detail").GetString());
        (await _data.BalanceAsync(theirs)).ShouldBe(90.00m);
        (await _data.CountEntriesAsync(mine)).ShouldBe(1);
    }

    [DockerFact]
    public async Task ReversalOnAnAccountThatDoesNotExist_IsAccountNotFound()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostReversalAsync(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            WriteClient.NewKey());

        response.ShouldBeProblem(404, "ACCOUNT_NOT_FOUND");
    }

    [DockerFact]
    public async Task ReversalOfAReversal_IsNotReversible()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");
        var reversal = await client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey());

        var again = await client.PostReversalAsync(accountId, reversal.Text("entryId"), WriteClient.NewKey());

        var problem = again.ShouldBeProblem(422, "ENTRY_NOT_REVERSIBLE");
        problem.GetProperty("title").GetString().ShouldBe("Lançamento não pode ser estornado");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(3);
    }

    [DockerFact]
    public async Task SecondReversalOfTheSameEntry_IsAConflictAndTheSameKeyIsAReplay()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");
        var firstKey = WriteClient.NewKey();
        var first = await client.PostReversalAsync(accountId, original.Text("entryId"), firstKey);

        var second = await client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey());
        var replay = await client.PostReversalAsync(accountId, original.Text("entryId"), firstKey);

        first.StatusCode.ShouldBe(201);
        var problem = second.ShouldBeProblem(409, "ENTRY_ALREADY_REVERSED");
        problem.GetProperty("title").GetString().ShouldBe("Lançamento já estornado");
        replay.StatusCode.ShouldBe(201, replay.Body);
        replay.Header("Idempotent-Replayed").ShouldBe("true");
        replay.Body.ShouldBe(first.Body);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(3);
        (await _data.BalanceAsync(accountId)).ShouldBe(100.00m);
    }

    [DockerFact]
    public async Task ReversalWithAnotherDescriptionOnTheSameKey_IsAReusedKey()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");
        var key = WriteClient.NewKey();

        (await client.PostReversalAsync(accountId, original.Text("entryId"), key, "{\"description\":\"a\"}"))
            .StatusCode.ShouldBe(201);

        (await client.PostReversalAsync(accountId, original.Text("entryId"), key, "{\"description\":\"b\"}"))
            .ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
    }

    [DockerTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"description\":null}")]
    [InlineData("{\"description\":\"  \"}")]
    public async Task Body_AbsentEmptyOrWithoutDescription_IsAcceptedWithoutDescription(string? body)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");

        var reversal = await client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey(), body);

        reversal.StatusCode.ShouldBe(201, reversal.Body);
        reversal.Json().GetProperty("description").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [DockerTheory]
    [InlineData("{\"amount\":\"5.00\"}", "amount")]
    [InlineData("{\"reference\":\"x\"}", "reference")]
    [InlineData("{\"type\":\"DEBIT\"}", "type")]
    public async Task Body_WithAnyFieldOtherThanDescription_IsRejectedAndNothingIsReversed(string body, string field)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");

        var reversal = await client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey(), body);

        reversal.ShouldBeValidationProblem().ShouldBe([(field, "UNKNOWN_FIELD")]);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }

    [DockerFact]
    public async Task Description_AboveOneHundredFortyCharacters_IsRejected()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");

        var reversal = await client.PostReversalAsync(
            accountId,
            original.Text("entryId"),
            WriteClient.NewKey(),
            $"{{\"description\":\"{new string('d', 141)}\"}}");

        reversal.ShouldBeValidationProblem().ShouldBe([("description", "TOO_LONG")]);
    }

    [DockerFact]
    public async Task Reversal_PublishesAnEventThatPointsToTheOriginal()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        var original = await client.CreditAsync(accountId, "25.00");
        var reversal = await client.PostReversalAsync(accountId, original.Text("entryId"), WriteClient.NewKey());

        var payloads = await _data.OutboxPayloadsAsync(accountId);

        payloads.Count.ShouldBe(2);

        using var created = JsonDocument.Parse(payloads[1]);

        created.RootElement.GetProperty("entryId").GetString().ShouldBe(reversal.Text("entryId"));
        created.RootElement.GetProperty("reversesEntryId").GetString().ShouldBe(original.Text("entryId"));
    }

    [DockerFact]
    public async Task ReversalAfterManyEntries_KeepsTheChainConsistent()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("300.00");
        var entries = new List<string>();

        for (var index = 0; index < 5; index++)
        {
            entries.Add((await client.DebitAsync(accountId, "10.00")).Text("entryId"));
        }

        foreach (var entryId in entries.Where((_, position) => position % 2 == 0))
        {
            (await client.PostReversalAsync(accountId, entryId, WriteClient.NewKey())).StatusCode.ShouldBe(201);
        }

        (await _data.BalanceAsync(accountId)).ShouldBe(280.00m);
        await _data.AssertConsistentAsync(accountId);
    }
}
