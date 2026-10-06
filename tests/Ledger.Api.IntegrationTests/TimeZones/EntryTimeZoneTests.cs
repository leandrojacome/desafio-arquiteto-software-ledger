using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.TimeZones;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class EntryTimeZoneTests(PostgresFixture postgres)
{
    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task Entry_WithAnyOffset_ReturnsOccurredAtInUtcWithSixFractionDigits()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("1000.00");
        (string Sent, string Expected)[] cases =
        [
            ("2026-10-01T11:03:10-03:00", "2026-10-01T14:03:10.000000Z"),
            ("2026-10-01T14:03:10Z", "2026-10-01T14:03:10.000000Z"),
            ("2026-10-01T14:03:10+00:00", "2026-10-01T14:03:10.000000Z"),
            ("2026-10-01T19:33:10+05:30", "2026-10-01T14:03:10.000000Z"),
            ("2026-10-01T11:03:10.5-03:00", "2026-10-01T14:03:10.500000Z"),
            ("2026-10-01T11:03:10.123456-03:00", "2026-10-01T14:03:10.123456Z"),
            ("2026-09-30T23:30:00-03:00", "2026-10-01T02:30:00.000000Z"),
            ("2026-10-01T00:30:00-03:00", "2026-10-01T03:30:00.000000Z"),
            ("2026-10-01T00:03:10-14:00", "2026-10-01T14:03:10.000000Z"),
            ("2026-10-02T04:03:10+14:00", "2026-10-01T14:03:10.000000Z")
        ];

        foreach (var (sent, expected) in cases)
        {
            var response = await client.PostEntryAsync(
                accountId,
                WriteClient.EntryBody("DEBIT", "1.00", occurredAt: sent),
                WriteClient.NewKey());

            response.StatusCode.ShouldBe(201, response.Body);
            response.Text("occurredAt").ShouldBe(expected);
            response.Text("recordedAt").ShouldEndWith("Z");
            response.Body.ShouldNotContain("-03:00");
        }
    }

    [DockerFact]
    public async Task Entry_WithTheBrasiliaOffset_CarriesTheUtcInstantToTheStatementAndToTheEvent()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var created = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "80.00", occurredAt: "2026-10-01T11:03:10.25-03:00", description: "Pix recebido"),
            WriteClient.NewKey());

        var statement = await client.SendAsync(HttpMethod.Get, $"/v1/accounts/{accountId}/entries?limit=10");
        var payloads = await _data.OutboxPayloadsAsync(accountId);

        created.StatusCode.ShouldBe(201, created.Body);
        statement.StatusCode.ShouldBe(200, statement.Body);

        var item = statement.Json().GetProperty("items").EnumerateArray().Single();
        using var payload = JsonDocument.Parse(payloads.Single());

        created.Text("occurredAt").ShouldBe("2026-10-01T14:03:10.250000Z");
        item.GetProperty("occurredAt").GetString().ShouldBe("2026-10-01T14:03:10.250000Z");
        payload.RootElement.GetProperty("occurredAt").GetString().ShouldBe("2026-10-01T14:03:10.250000Z");
        payload.RootElement.GetProperty("recordedAt").GetString().ShouldEndWith("Z");
        statement.Body.ShouldNotContain("-03:00");
        payloads.Single().ShouldNotContain("-03:00");
    }

    [DockerFact]
    public async Task Replay_WithTheSameInstantInDifferentOffsets_IsTheSameRequestWithTheSameHash()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();
        string[] spellings =
        [
            "2026-10-01T11:03:10-03:00",
            "2026-10-01T14:03:10+00:00",
            "2026-10-01T14:03:10.000000Z",
            "2026-10-01T14:03:10.0Z",
            "2026-10-01T19:33:10+05:30",
            "2026-10-01T00:03:10-14:00",
            "2026-10-02T04:03:10+14:00"
        ];

        var first = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("DEBIT", "80.00", occurredAt: "2026-10-01T14:03:10Z", reference: "ref-tz-1"),
            key);

        first.StatusCode.ShouldBe(201, first.Body);

        foreach (var spelling in spellings)
        {
            var replay = await client.PostEntryAsync(
                accountId,
                WriteClient.EntryBody("DEBIT", "80.00", occurredAt: spelling, reference: "ref-tz-1"),
                key);

            replay.StatusCode.ShouldBe(201, replay.Body);
            replay.Header("Idempotent-Replayed").ShouldBe("true");
            replay.Body.ShouldBe(first.Body);
        }

        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
        (await _data.CountOutboxAsync(accountId)).ShouldBe(2);
        (await _data.BalanceAsync(accountId)).ShouldBe(420.00m);
    }

    [DockerFact]
    public async Task Replay_FirstSentInBrasiliaAndRepeatedInUtc_IsTheSameRequest()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();

        var first = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("DEBIT", "80.00", occurredAt: "2026-10-01T11:03:10-03:00"),
            key);
        var replay = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("DEBIT", "80.00", occurredAt: "2026-10-01T14:03:10Z"),
            key);

        first.StatusCode.ShouldBe(201, first.Body);
        replay.StatusCode.ShouldBe(201, replay.Body);
        replay.Header("Idempotent-Replayed").ShouldBe("true");
        replay.Body.ShouldBe(first.Body);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }

    [DockerFact]
    public async Task SameKeyWithAnotherInstantInAnyOffset_IsRefusedAsReusedAndWritesNothing()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();
        string[] otherInstants =
        [
            "2026-10-01T11:03:11-03:00",
            "2026-10-01T14:03:10-03:00",
            "2026-10-01T14:03:10.000001Z",
            "2026-10-01T11:03:09.999999-03:00",
            "2026-10-01T14:03:10+05:30",
            "2026-10-01T14:03:10+00:01"
        ];

        (await client.PostEntryAsync(
                accountId,
                WriteClient.EntryBody("DEBIT", "10.00", occurredAt: "2026-10-01T14:03:10Z"),
                key))
            .StatusCode.ShouldBe(201);

        foreach (var instant in otherInstants)
        {
            var reused = await client.PostEntryAsync(
                accountId,
                WriteClient.EntryBody("DEBIT", "10.00", occurredAt: instant),
                key);

            reused.ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
            reused.HasHeader("Idempotent-Replayed").ShouldBeFalse();
        }

        var withoutInstant = await client.PostEntryAsync(accountId, WriteClient.EntryBody("DEBIT", "10.00"), key);

        withoutInstant.ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
        (await _data.BalanceAsync(accountId)).ShouldBe(490.00m);
    }

    [DockerFact]
    public async Task Entry_WithoutTimeZone_IsRefusedWithMissingTimeZoneAndWritesNothing()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        var key = WriteClient.NewKey();

        var response = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "10.00", occurredAt: "2026-10-01T14:03:10"),
            key);

        response.ShouldBeValidationProblem().ShouldBe([("occurredAt", "MISSING_TIME_ZONE")]);

        var error = response.Json().GetProperty("errors").EnumerateArray().Single();

        error.GetProperty("message").GetString()
            .ShouldBe("Informe o fuso horário no campo 'occurredAt', por exemplo 'Z' ou '-03:00'.");
        response.Body.ShouldNotContain("14:03:10");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(0);
        (await _data.CountKeysAsync(accountId)).ShouldBe(0);
        (await _data.CountOutboxAsync(accountId)).ShouldBe(0);
    }

    [DockerTheory]
    [InlineData("2026-10-01T14:03:10+25:00")]
    [InlineData("2026-10-01T14:03:10-15:00")]
    [InlineData("2026-10-01T14:03:10+14:01")]
    [InlineData("2026-10-01T14:03:10-00:00")]
    [InlineData("2026-10-01T14:03:10+0300")]
    [InlineData("2026-10-01T14:03:10.1234567-03:00")]
    public async Task Entry_WithAnInvalidOffsetOrShape_IsRefusedAsAnInvalidFormatAndWritesNothing(string occurredAt)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "10.00", occurredAt: occurredAt),
            WriteClient.NewKey());

        response.ShouldBeValidationProblem().ShouldBe([("occurredAt", "INVALID_FORMAT")]);

        var message = response.Json().GetProperty("errors").EnumerateArray().Single().GetProperty("message").GetString();

        message.ShouldNotBeNull().ShouldContain("ISO 8601");
        message.ShouldContain("'Z'");
        message.ShouldContain("'-03:00'");
        response.Body.ShouldNotContain("25:00");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(0);
    }

    [DockerFact]
    public async Task Entry_AtTheFirstInstantOf1970_IsStoredAsTheInstantAndNeverAsInfinity()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "10.00", occurredAt: "1969-12-31T21:00:00-03:00"),
            WriteClient.NewKey());

        response.StatusCode.ShouldBe(201, response.Body);
        response.Text("occurredAt").ShouldBe("1970-01-01T00:00:00.000000Z");

        var stored = await _data.LastOccurredAtAsync(accountId);

        stored.IsFinite.ShouldBeTrue();
        stored.Utc.ShouldBe("1970-01-01 00:00:00");
    }

    [DockerTheory]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("0001-01-01T00:00:00+00:00")]
    [InlineData("0001-01-01T00:00:00.0000000+00:00")]
    [InlineData("0001-01-01T00:00:00-03:00")]
    [InlineData("1969-12-31T23:59:59.999999Z")]
    public async Task Entry_BeforeTheFirstInstantOf1970_IsRefusedAsOutOfRangeAndWritesNothing(string occurredAt)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "10.00", occurredAt: occurredAt),
            WriteClient.NewKey());

        response.ShouldBeValidationProblem().ShouldBe([("occurredAt", "OUT_OF_RANGE")]);
        response.Json().GetProperty("errors").EnumerateArray().Single().GetProperty("message").GetString()
            .ShouldBe("O campo 'occurredAt' está fora da faixa permitida.");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(0);
        (await _data.CountKeysAsync(accountId)).ShouldBe(0);
        (await _data.CountOutboxAsync(accountId)).ShouldBe(0);
    }

    [DockerFact]
    public async Task Entry_WithTheRoundTripFormatOfDotNet_IsAcceptedWhenTheSeventhDigitIsZeroAndRefusedOtherwise()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var accepted = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "10.00", occurredAt: "2026-10-01T11:03:10.5654540-03:00"),
            WriteClient.NewKey());
        var refused = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "10.00", occurredAt: "2026-10-01T11:03:10.5654541-03:00"),
            WriteClient.NewKey());

        accepted.StatusCode.ShouldBe(201, accepted.Body);
        accepted.Text("occurredAt").ShouldBe("2026-10-01T14:03:10.565454Z");
        refused.ShouldBeValidationProblem().ShouldBe([("occurredAt", "INVALID_FORMAT")]);
        refused.Json().GetProperty("errors").EnumerateArray().Single().GetProperty("message").GetString()
            .ShouldBe(
                "O campo 'occurredAt' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo.");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(1);
    }

    [DockerFact]
    public async Task Replay_WithTheSameInstantWrittenWithSevenDigitsOfZeroPadding_IsTheSameRequest()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");
        var key = WriteClient.NewKey();

        var first = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("DEBIT", "80.00", occurredAt: "2026-10-01T11:03:10.565454-03:00"),
            key);
        var replay = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("DEBIT", "80.00", occurredAt: "2026-10-01T14:03:10.5654540Z"),
            key);

        first.StatusCode.ShouldBe(201, first.Body);
        replay.StatusCode.ShouldBe(201, replay.Body);
        replay.Header("Idempotent-Replayed").ShouldBe("true");
        replay.Body.ShouldBe(first.Body);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }

    [DockerFact]
    public async Task OccurredAt_AheadOfTheClockIsJudgedInUtcWhateverTheOffsetOfTheText()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero));
        await using var factory = new WriteApiFactory(postgres, time: time);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        string[] accepted =
        [
            "2026-10-01T11:08:11-03:00",
            "2026-10-01T19:38:11+05:30",
            "2026-10-01T23:03:11+14:00",
            "2026-10-01T00:00:00-14:00"
        ];
        string[] refused =
        [
            "2026-10-01T11:08:11.000001-03:00",
            "2026-10-01T19:38:11.000001+05:30",
            "2026-10-01T05:08:12-14:00"
        ];

        foreach (var occurredAt in accepted)
        {
            var response = await client.PostEntryAsync(
                accountId,
                WriteClient.EntryBody("CREDIT", "1.00", occurredAt: occurredAt),
                WriteClient.NewKey());

            response.StatusCode.ShouldBe(201, $"{occurredAt}: {response.Body}");
        }

        foreach (var occurredAt in refused)
        {
            var response = await client.PostEntryAsync(
                accountId,
                WriteClient.EntryBody("CREDIT", "1.00", occurredAt: occurredAt),
                WriteClient.NewKey());

            response.ShouldBeValidationProblem().ShouldBe([("occurredAt", "IN_THE_FUTURE")]);
        }

        (await _data.CountEntriesAsync(accountId)).ShouldBe(accepted.Length);
    }

    [DockerFact]
    public async Task Entry_WithTheSameInstantInTwoOffsetsInTwoRequests_RecordsTheSameOccurredAtInBoth()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var inBrasilia = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "10.00", occurredAt: "2026-10-01T11:03:10-03:00"),
            WriteClient.NewKey());
        var inUtc = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "10.00", occurredAt: "2026-10-01T14:03:10Z"),
            WriteClient.NewKey());

        inBrasilia.StatusCode.ShouldBe(201, inBrasilia.Body);
        inUtc.StatusCode.ShouldBe(201, inUtc.Body);
        inBrasilia.Text("occurredAt").ShouldBe(inUtc.Text("occurredAt"));
        inBrasilia.Text("entryId").ShouldNotBe(inUtc.Text("entryId"));
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
    }
}
