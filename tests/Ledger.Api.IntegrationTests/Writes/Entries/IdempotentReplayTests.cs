using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class IdempotentReplayTests(PostgresFixture postgres)
{
    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task Replay_ReturnsTheSameStatusAndTheSameBodyWithTheReplayedHeader()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();
        var body = WriteClient.EntryBody("DEBIT", "80.00", description: "Pix enviado", reference: "ref-0001");

        var first = await client.PostEntryAsync(accountId, body, key);
        var second = await client.PostEntryAsync(
            accountId,
            body,
            key,
            new WriteRequestOptions { CorrelationId = "second-call-correlation-id" });

        first.StatusCode.ShouldBe(201, first.Body);
        second.StatusCode.ShouldBe(201, second.Body);
        second.Body.ShouldBe(first.Body);
        first.HasHeader("Idempotent-Replayed").ShouldBeFalse();
        second.Header("Idempotent-Replayed").ShouldBe("true");
        second.Header("Cache-Control").ShouldBe("no-store");
        second.Header("X-Correlation-Id").ShouldBe("second-call-correlation-id");
        second.Header("Location").ShouldBe(first.Header("Location"));
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
        (await _data.CountOutboxAsync(accountId)).ShouldBe(2);
        (await _data.BalanceAsync(accountId)).ShouldBe(420.00m);
    }

    [DockerFact]
    public async Task Replay_AfterTheBalanceChanged_StillReturnsTheOriginalEntry()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();
        var first = await client.DebitAsync(accountId, "80.00", key);

        (await client.CreditAsync(accountId, "1.00")).StatusCode.ShouldBe(201);
        (await client.DebitAsync(accountId, "20.00")).StatusCode.ShouldBe(201);

        var replay = await client.DebitAsync(accountId, "80.00", key);

        replay.StatusCode.ShouldBe(201, replay.Body);
        replay.Body.ShouldBe(first.Body);
        replay.Text("balanceAfter").ShouldBe("420.00");
        (await _data.BalanceAsync(accountId)).ShouldBe(401.00m);
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task Replay_WithSurplusWhitespaceInDescriptionAndNumericSpelling_IsStillTheSameRequest()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();

        var first = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("DEBIT", "80", description: "Pix enviado"),
            key);
        var replay = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("DEBIT", "80.00", description: "  Pix enviado  "),
            key);

        first.StatusCode.ShouldBe(201, first.Body);
        replay.StatusCode.ShouldBe(201, replay.Body);
        replay.Header("Idempotent-Replayed").ShouldBe("true");
        replay.Body.ShouldBe(first.Body);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }

    [DockerFact]
    public async Task Replay_WithPropertiesInAnotherOrderAndTheSameInstantInAnotherOffset_IsStillTheSameRequest()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();

        var first = await client.PostEntryAsync(
            accountId,
            "{\"type\":\"DEBIT\",\"amount\":\"80.00\",\"currency\":\"BRL\",\"occurredAt\":\"2026-10-01T14:03:10Z\",\"reference\":\"ref-1\"}",
            key);
        var replay = await client.PostEntryAsync(
            accountId,
            "{\"reference\":\"ref-1\",\"occurredAt\":\"2026-10-01T11:03:10-03:00\",\"currency\":\"BRL\",\"amount\":\"80\",\"type\":\"DEBIT\"}",
            key);

        first.StatusCode.ShouldBe(201, first.Body);
        replay.StatusCode.ShouldBe(201, replay.Body);
        replay.Header("Idempotent-Replayed").ShouldBe("true");
        replay.Body.ShouldBe(first.Body);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }

    [DockerFact]
    public async Task SameKeyWithAnotherOccurredAtOrCurrency_IsRefusedAsReused()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();

        (await client.PostEntryAsync(accountId, WriteClient.EntryBody("DEBIT", "10.00", occurredAt: "2026-10-01T14:03:10Z"), key))
            .StatusCode.ShouldBe(201);

        (await client.PostEntryAsync(accountId, WriteClient.EntryBody("DEBIT", "10.00", occurredAt: "2026-10-01T14:03:11Z"), key))
            .ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
        (await client.PostEntryAsync(accountId, WriteClient.EntryBody("DEBIT", "10.00", occurredAt: "2026-10-01T14:03:10Z", description: "x"), key))
            .ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
        (await client.PostEntryAsync(accountId, WriteClient.EntryBody("DEBIT", "10.00", "EUR", occurredAt: "2026-10-01T14:03:10Z"), key))
            .ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }

    [DockerFact]
    public async Task SameKeyWithAnotherAmount_IsRefusedAsReusedAndChangesNothing()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();

        (await client.DebitAsync(accountId, "80.00", key)).StatusCode.ShouldBe(201);

        var reused = await client.DebitAsync(accountId, "90.00", key);

        var problem = reused.ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
        problem.GetProperty("title").GetString().ShouldBe("Chave de idempotência reutilizada em outra requisição");
        reused.HasHeader("Idempotent-Replayed").ShouldBeFalse();
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
        (await _data.BalanceAsync(accountId)).ShouldBe(420.00m);
    }

    [DockerTheory]
    [InlineData("CREDIT", "80.00", "BRL")]
    [InlineData("DEBIT", "80.01", "BRL")]
    public async Task SameKeyWithAnotherTypeOrAmount_IsRefusedAsReused(string type, string amount, string currency)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();

        (await client.DebitAsync(accountId, "80.00", key)).StatusCode.ShouldBe(201);

        var reused = await client.PostEntryAsync(accountId, WriteClient.EntryBody(type, amount, currency), key);

        reused.ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
    }

    [DockerFact]
    public async Task SameKeyWithAnotherDescriptionOrReference_IsRefusedAsReused()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();

        (await client.PostEntryAsync(accountId, WriteClient.EntryBody("DEBIT", "10.00", description: "a"), key))
            .StatusCode.ShouldBe(201);

        (await client.PostEntryAsync(accountId, WriteClient.EntryBody("DEBIT", "10.00", description: "b"), key))
            .ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
        (await client.PostEntryAsync(accountId, WriteClient.EntryBody("DEBIT", "10.00", description: "a", reference: "r1"), key))
            .ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
    }

    [DockerFact]
    public async Task ReusedKey_ReportsNothingAboutWhatChanged()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();
        await client.DebitAsync(accountId, "80.00", key);

        var reused = await client.DebitAsync(accountId, "90.00", key);

        reused.Body.ShouldNotContain("80.00");
        reused.Body.ShouldNotContain("90.00");
        reused.Body.ShouldNotContain(key);
    }

    [DockerFact]
    public async Task TheSameKey_OnAnotherAccount_IsIndependent()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var first = await client.CreateFundedAccountAsync("100.00");
        var second = await client.CreateFundedAccountAsync("100.00");
        var key = WriteClient.NewKey();

        var onFirst = await client.DebitAsync(first, "10.00", key);
        var onSecond = await client.DebitAsync(second, "10.00", key);

        onFirst.StatusCode.ShouldBe(201);
        onSecond.StatusCode.ShouldBe(201);
        onSecond.HasHeader("Idempotent-Replayed").ShouldBeFalse();
        onSecond.Text("entryId").ShouldNotBe(onFirst.Text("entryId"));
        (await _data.BalanceAsync(second)).ShouldBe(90.00m);
    }

    [DockerFact]
    public async Task TheSameKey_ForARegistrationAndAReversal_IsRefusedAsReused()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();
        var original = await client.DebitAsync(accountId, "10.00", key);

        var reversal = await client.PostReversalAsync(accountId, original.Text("entryId"), key);

        reversal.ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }
}
