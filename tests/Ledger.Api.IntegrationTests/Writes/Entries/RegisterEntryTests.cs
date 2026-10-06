using System.Text.RegularExpressions;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed partial class RegisterEntryTests(PostgresFixture postgres)
{
    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task Credit_OnAnAccountWithBalance_ReturnsCreatedWithTheNewBalanceAndTheNextVersion()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");

        var response = await client.CreditAsync(accountId, "25.50");

        response.StatusCode.ShouldBe(201, response.Body);
        response.ContentType.ShouldBe("application/json");
        response.Header("Cache-Control").ShouldBe("no-store");
        response.Header("Location").ShouldBe($"/v1/accounts/{accountId}/entries");
        response.HasHeader("Idempotent-Replayed").ShouldBeFalse();

        var entry = response.Json();

        entry.GetProperty("accountId").GetString().ShouldBe(accountId);
        entry.GetProperty("type").GetString().ShouldBe("CREDIT");
        entry.GetProperty("amount").GetString().ShouldBe("25.50");
        entry.GetProperty("currency").GetString().ShouldBe("BRL");
        entry.GetProperty("balanceAfter").GetString().ShouldBe("125.50");
        entry.GetProperty("accountVersion").GetInt64().ShouldBe(2);
        entry.GetProperty("reversesEntryId").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        entry.GetProperty("entryId").GetString().ShouldNotBeNull().ShouldBeUuidText();
        InstantShape().IsMatch(entry.GetProperty("recordedAt").GetString() ?? string.Empty).ShouldBeTrue();
        (await _data.BalanceAsync(accountId)).ShouldBe(125.50m);
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task Debit_OfTheWholeBalance_IsAcceptedAndLeavesZero()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");

        var response = await client.DebitAsync(accountId, "100.00");

        response.StatusCode.ShouldBe(201, response.Body);
        response.Text("balanceAfter").ShouldBe("0.00");
        response.Text("type").ShouldBe("DEBIT");
        (await _data.BalanceAsync(accountId)).ShouldBe(0m);
    }

    [DockerFact]
    public async Task Debit_WithinTheOverdraftLimit_TakesTheBalanceBelowZero()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("50.00", overdraftLimit: "200.00");

        var response = await client.DebitAsync(accountId, "250.00");

        response.StatusCode.ShouldBe(201, response.Body);
        response.Text("balanceAfter").ShouldBe("-200.00");
        (await client.DebitAsync(accountId, "0.01")).ShouldBeProblem(422, "INSUFFICIENT_FUNDS");
    }

    [DockerFact]
    public async Task Entry_WithEveryOptionalField_ReturnsThemNormalized()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("1000.00");
        var body = WriteClient.EntryBody(
            "DEBIT",
            "80",
            occurredAt: "2026-10-01T11:03:10-03:00",
            description: "  Pix enviado ",
            reference: "E18236120202610011403s0a1b2c3d4e");

        var response = await client.PostEntryAsync(accountId, body, WriteClient.NewKey());

        response.StatusCode.ShouldBe(201, response.Body);
        response.Text("amount").ShouldBe("80.00");
        response.Text("balanceAfter").ShouldBe("920.00");
        response.Text("occurredAt").ShouldBe("2026-10-01T14:03:10.000000Z");
        response.Text("description").ShouldBe("Pix enviado");
        response.Text("reference").ShouldBe("E18236120202610011403s0a1b2c3d4e");
    }

    [DockerFact]
    public async Task Entry_WithoutOccurredAt_RecordsItAtTheInstantOfTheRegistration()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.CreditAsync(accountId, "10.00");

        response.StatusCode.ShouldBe(201, response.Body);
        response.Text("occurredAt").ShouldBe(response.Text("recordedAt"));
        response.Json().GetProperty("description").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        response.Json().GetProperty("reference").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [DockerFact]
    public async Task Entry_RecordsTheCallerAndTheCorrelationIdOfTheRequest()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        const string correlation = "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10";
        var token = TestTokenFactory.Create(clientId: "pix-gateway");

        var response = await client.PostEntryAsync(
            accountId,
            WriteClient.EntryBody("CREDIT", "10.00"),
            WriteClient.NewKey(),
            new WriteRequestOptions { Token = token, CorrelationId = correlation });

        response.StatusCode.ShouldBe(201, response.Body);
        response.Header("X-Correlation-Id").ShouldBe(correlation);

        var row = (await _data.Queries.EntriesAsync(WriteTestData.Account(accountId), CancellationToken.None)).Single();

        row.ClientId.ShouldBe("pix-gateway");
        row.CorrelationId.ShouldBe(correlation);
    }

    [DockerFact]
    public async Task Entry_Accepted_LeavesExactlyOneEventInTheOutbox()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        (await _data.CountOutboxAsync(accountId)).ShouldBe(0);

        var response = await client.CreditAsync(accountId, "10.00");

        response.StatusCode.ShouldBe(201, response.Body);
        (await _data.CountOutboxAsync(accountId)).ShouldBe(1);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(1);
        (await _data.CountKeysAsync(accountId)).ShouldBe(1);
    }

    [DockerFact]
    public async Task ManySequentialEntries_ChainTheBalancesAndKeepTheInvariants()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        var expected = 0m;

        for (var index = 1; index <= 20; index++)
        {
            var isCredit = index % 3 != 0;
            var response = isCredit
                ? await client.CreditAsync(accountId, "30.00")
                : await client.DebitAsync(accountId, "10.00");

            expected += isCredit ? 30.00m : -10.00m;
            response.StatusCode.ShouldBe(201, response.Body);
            response.Json().GetProperty("accountVersion").GetInt64().ShouldBe(index);
            response.Text("balanceAfter").ShouldBe(expected.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }

        (await _data.BalanceAsync(accountId)).ShouldBe(expected);
        await _data.AssertConsistentAsync(accountId);
    }

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{6}Z\\z", RegexOptions.CultureInvariant)]
    private static partial Regex InstantShape();
}
