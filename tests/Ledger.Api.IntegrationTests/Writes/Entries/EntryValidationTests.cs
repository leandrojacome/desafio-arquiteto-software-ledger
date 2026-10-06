using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class EntryValidationTests(PostgresFixture postgres)
{
    private static readonly string[] IssueProperties = ["field", "reason", "message"];

    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task ThreeWrongFields_AreReportedTogetherInOneBadRequest()
    {
        var time = new FakeTimeProvider(TimeProvider.System.GetUtcNow());
        await using var factory = new WriteApiFactory(postgres, time: time);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var occurredAt = time.GetUtcNow().AddMinutes(10).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var body = WriteClient.EntryBody("TRANSFER", "0.001", occurredAt: occurredAt);

        var response = await client.PostEntryAsync(accountId, body, WriteClient.NewKey());

        var errors = response.ShouldBeValidationProblem();

        errors.ShouldBe([("type", "NOT_ALLOWED"), ("amount", "TOO_MANY_DECIMALS"), ("occurredAt", "IN_THE_FUTURE")]);

        var items = response.Json().GetProperty("errors").EnumerateArray().ToList();

        items.Select(item => item.EnumerateObject().Select(property => property.Name).ToList())
            .ShouldAllBe(names => names.SequenceEqual(IssueProperties));
        items.Select(item => item.GetProperty("message").GetString() ?? string.Empty)
            .ShouldAllBe(message => !message.Contains("TRANSFER", StringComparison.Ordinal)
                                    && !message.Contains("0.001", StringComparison.Ordinal));
    }

    [DockerFact]
    public async Task OccurredAt_ExactlyFiveMinutesAhead_IsAcceptedAndOneMicrosecondMoreIsNot()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero));
        await using var factory = new WriteApiFactory(postgres, time: time);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var accepted = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "10.00", occurredAt: "2026-10-01T14:08:11.000000Z"),
            WriteClient.NewKey());
        var refused = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "10.00", occurredAt: "2026-10-01T14:08:11.000001Z"),
            WriteClient.NewKey());

        accepted.StatusCode.ShouldBe(201, accepted.Body);
        accepted.Text("occurredAt").ShouldBe("2026-10-01T14:08:11.000000Z");
        refused.ShouldBeValidationProblem().ShouldBe([("occurredAt", "IN_THE_FUTURE")]);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(1);
    }

    [DockerFact]
    public async Task OccurredAt_InThePast_IsAcceptedAndDoesNotMoveTheRecordedInstant()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "10.00", occurredAt: "2001-01-01T00:00:00Z"),
            WriteClient.NewKey());

        response.StatusCode.ShouldBe(201, response.Body);
        response.Text("occurredAt").ShouldBe("2001-01-01T00:00:00.000000Z");
        string.CompareOrdinal(response.Text("recordedAt"), "2026-01-01").ShouldBeGreaterThan(0);
    }

    [DockerTheory]
    [InlineData("{\"type\":\"CREDIT\",\"amount\":10.00,\"currency\":\"BRL\"}", "amount", "INVALID_FORMAT")]
    [InlineData("{\"type\":\"CREDIT\",\"amount\":\"-1.00\",\"currency\":\"BRL\"}", "amount", "OUT_OF_RANGE")]
    [InlineData("{\"type\":\"CREDIT\",\"amount\":\"0.00\",\"currency\":\"BRL\"}", "amount", "OUT_OF_RANGE")]
    [InlineData("{\"type\":\"CREDIT\",\"amount\":\"1000000000.00\",\"currency\":\"BRL\"}", "amount", "OUT_OF_RANGE")]
    [InlineData("{\"type\":\"CREDIT\",\"amount\":\"1.001\",\"currency\":\"BRL\"}", "amount", "TOO_MANY_DECIMALS")]
    [InlineData("{\"type\":\"credit\",\"amount\":\"1.00\",\"currency\":\"BRL\"}", "type", "NOT_ALLOWED")]
    [InlineData("{\"amount\":\"1.00\",\"currency\":\"BRL\"}", "type", "REQUIRED")]
    [InlineData("{\"type\":\"CREDIT\",\"amount\":\"1.00\"}", "currency", "REQUIRED")]
    [InlineData("{\"type\":\"CREDIT\",\"amount\":\"1.00\",\"currency\":\"brl\"}", "currency", "INVALID_FORMAT")]
    [InlineData("{\"type\":\"CREDIT\",\"amount\":\"1.00\",\"currency\":\"BRL\",\"occurredAt\":\"2026-10-01\"}", "occurredAt", "INVALID_FORMAT")]
    [InlineData("{\"type\":\"CREDIT\",\"amount\":\"1.00\",\"currency\":\"BRL\",\"reference\":\"has space\"}", "reference", "INVALID_FORMAT")]
    [InlineData("{\"type\":\"CREDIT\",\"amount\":\"1.00\",\"currency\":\"BRL\",\"accountId\":\"x\"}", "accountId", "UNKNOWN_FIELD")]
    [InlineData("{\"type\":\"CREDIT\",\"amount\":\"1.00\",\"currency\":\"BRL\",\"balance\":\"x\"}", "balance", "UNKNOWN_FIELD")]
    [InlineData("", "$", "INVALID_JSON")]
    [InlineData("null", "$", "INVALID_JSON")]
    [InlineData("[]", "$", "INVALID_JSON")]
    [InlineData("{broken", "$", "INVALID_JSON")]
    [InlineData("{\"type\":\"CREDIT\",\"type\":\"DEBIT\",\"amount\":\"1.00\",\"currency\":\"BRL\"}", "$", "INVALID_JSON")]
    public async Task InvalidBody_IsRejectedWithTheReasonOfTheFieldAndWritesNothing(string body, string field, string reason)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(accountId, body, WriteClient.NewKey());

        response.ShouldBeValidationProblem().ShouldBe([(field, reason)]);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(0);
        (await _data.CountKeysAsync(accountId)).ShouldBe(0);
        (await _data.CountOutboxAsync(accountId)).ShouldBe(0);
    }

    [DockerFact]
    public async Task DescriptionAndReferenceAtTheirLimits_AreAcceptedAndOneMoreIsNot()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var accepted = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00", description: new string('d', 140), reference: new string('r', 100)),
            WriteClient.NewKey());
        var refused = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "1.00", description: new string('d', 141), reference: new string('r', 101)),
            WriteClient.NewKey());

        accepted.StatusCode.ShouldBe(201, accepted.Body);
        refused.ShouldBeValidationProblem().ShouldBe([("description", "TOO_LONG"), ("reference", "TOO_LONG")]);
    }

    [DockerTheory]
    [InlineData("has space")]
    [InlineData("accentç")]
    [InlineData("tab	here")]
    public async Task IdempotencyKey_Malformed_IsReportedAsAFieldOfTheHeader(string key)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00"), key);

        response.ShouldBeValidationProblem().ShouldBe([("Idempotency-Key", "INVALID_FORMAT")]);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(0);
    }

    [DockerFact]
    public async Task IdempotencyKey_Empty_IsReportedAsMissing()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00"), string.Empty);

        response.ShouldBeProblem(400, "IDEMPOTENCY_KEY_REQUIRED");
    }

    [DockerFact]
    public async Task IdempotencyKey_OfOneHundredTwentyEightCharacters_IsAcceptedAndOneMoreIsTooLong()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var accepted = await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00"), new string('k', 128));
        var refused = await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00"), new string('k', 129));

        accepted.StatusCode.ShouldBe(201, accepted.Body);
        refused.ShouldBeValidationProblem().ShouldBe([("Idempotency-Key", "TOO_LONG")]);
    }

    [DockerFact]
    public async Task IdempotencyKey_SentTwice_IsReportedAsAnInvalidFormat()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        var options = new WriteRequestOptions { RepeatedIdempotencyKeys = ["first-key-0001", "second-key-0002"] };

        var response = await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "1.00"), null, options);

        response.ShouldBeValidationProblem().ShouldBe([("Idempotency-Key", "INVALID_FORMAT")]);
    }

    [DockerFact]
    public async Task BodyAndHeaderProblems_ShareTheSameList()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(accountId, WriteClient.EntryBody("DEBIT", "-5"), "bad key");

        response.ShouldBeValidationProblem().ShouldBe([("amount", "OUT_OF_RANGE"), ("Idempotency-Key", "INVALID_FORMAT")]);
    }
}
