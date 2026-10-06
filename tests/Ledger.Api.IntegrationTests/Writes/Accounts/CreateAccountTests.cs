using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Accounts;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class CreateAccountTests(PostgresFixture postgres)
{
    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task Cpf_WithCurrencyAndNoLimit_CreatesTheAccountWithZeroBalanceAndTheMaskedDocument()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostAccountAsync("{\"holderDocument\":\"123.456.789-09\",\"currency\":\"BRL\"}");

        response.StatusCode.ShouldBe(201, response.Body);
        response.ContentType.ShouldBe("application/json");
        response.Header("Cache-Control").ShouldBe("no-store");

        var account = response.Json();

        account.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["accountId", "currency", "overdraftLimit", "holderDocumentMasked", "createdAt"]);
        account.GetProperty("accountId").GetString().ShouldNotBeNull().ShouldBeUuidText();
        account.GetProperty("currency").GetString().ShouldBe("BRL");
        account.GetProperty("overdraftLimit").GetString().ShouldBe("0.00");
        account.GetProperty("holderDocumentMasked").GetString().ShouldBe("***.***.789-**");
        account.GetProperty("createdAt").GetString().ShouldNotBeNull().ShouldMatch("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{6}Z$");
        response.Header("Location").ShouldBe($"/v1/accounts/{response.Text("accountId")}/balance");
        response.Body.ShouldNotContain("12345678909");
        response.Body.ShouldNotContain("123.456.789-09");

        var balance = await client.SendAsync(HttpMethod.Get, response.Header("Location") ?? string.Empty);

        balance.StatusCode.ShouldBe(200, balance.Body);
        balance.Text("balance").ShouldBe("0.00");

        var stored = await _data.StoredAccountAsync(response.Text("accountId"));

        stored.Balance.ShouldBe(0m);
        stored.Version.ShouldBe(0);
        stored.OverdraftLimit.ShouldBe(0m);
        stored.Currency.ShouldBe("BRL");
    }

    [DockerTheory]
    [InlineData("12.345.678/0001-95", "**.***.***/0001-**")]
    [InlineData("12345678000195", "**.***.***/0001-**")]
    [InlineData("12ABC34501DE35", "**.***.***/01DE-**")]
    [InlineData("12abc34501de35", "**.***.***/01DE-**")]
    [InlineData("12.ABC.345/01DE-35", "**.***.***/01DE-**")]
    [InlineData("12345678909", "***.***.789-**")]
    public async Task HolderDocument_OfEitherKindAndFormat_IsAcceptedAndMasked(string document, string mask)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostAccountAsync(WriteClient.AccountBody(document));

        response.StatusCode.ShouldBe(201, response.Body);
        response.Text("holderDocumentMasked").ShouldBe(mask);
    }

    [DockerTheory]
    [InlineData("50.5", "50.50")]
    [InlineData("50", "50.00")]
    [InlineData("0", "0.00")]
    [InlineData("999999999.99", "999999999.99")]
    public async Task OverdraftLimit_IsStoredAndReturnedWithTwoDecimalPlaces(string sent, string expected)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostAccountAsync(WriteClient.AccountBody(overdraftLimit: sent));

        response.StatusCode.ShouldBe(201, response.Body);
        response.Text("overdraftLimit").ShouldBe(expected);
        (await _data.StoredAccountAsync(response.Text("accountId"))).OverdraftLimit
            .ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [DockerFact]
    public async Task NullOverdraftLimit_IsTheSameAsAnAbsentOne()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.PostAccountAsync(
            "{\"holderDocument\":\"123.456.789-09\",\"currency\":\"BRL\",\"overdraftLimit\":null}");

        response.StatusCode.ShouldBe(201, response.Body);
        response.Text("overdraftLimit").ShouldBe("0.00");
    }

    [DockerFact]
    public async Task NewAccount_AcceptsAnEntryRightAway_AndTheOverdraftLimitIsRespected()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync(overdraftLimit: "100.00");

        var debit = await client.DebitAsync(accountId, "100.00");
        var refused = await client.DebitAsync(accountId, "0.01");

        debit.StatusCode.ShouldBe(201, debit.Body);
        debit.Text("balanceAfter").ShouldBe("-100.00");
        debit.Json().GetProperty("accountVersion").GetInt64().ShouldBe(1);
        refused.ShouldBeProblem(422, "INSUFFICIENT_FUNDS");
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task EntryOfANewAccount_IsRecordedAfterTheMomentOfTheCreation()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var created = await client.PostAccountAsync();

        var entry = await client.CreditAsync(created.Text("accountId"), "1.00");

        string.CompareOrdinal(entry.Text("recordedAt"), created.Text("createdAt")).ShouldBeGreaterThan(0);
    }

    [DockerFact]
    public async Task Creation_GeneratesNoEventAndNoEntry()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var accountId = await client.CreateAccountAsync();

        (await _data.CountOutboxAsync(accountId)).ShouldBe(0);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(0);
        (await _data.CountKeysAsync(accountId)).ShouldBe(0);
    }

    [DockerFact]
    public async Task AuditTrail_RecordsTheCreationWithTheCallerOfTheTokenAndTheCorrelationId()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        const string correlation = "5d1b7c0e9a3f4c28b6e1d04f7a92c3b8";
        var token = TestTokenFactory.Create(clientId: "backoffice-admin");

        var response = await client.PostAccountAsync(
            null,
            new WriteRequestOptions { Token = token, CorrelationId = correlation });

        response.StatusCode.ShouldBe(201, response.Body);
        response.Header("X-Correlation-Id").ShouldBe(correlation);

        var row = (await _data.AuditRowsAsync(response.Text("accountId"), "account.created")).ShouldHaveSingleItem();

        row.ClientId.ShouldBe("backoffice-admin");
        row.CorrelationId.ShouldBe(correlation);
        row.Outcome.ShouldBe("SUCCESS");
        using var details = JsonDocument.Parse(row.Details);

        details.RootElement.EnumerateObject().ShouldBeEmpty();
    }

    [DockerTheory]
    [InlineData("clientId")]
    [InlineData("accountId")]
    [InlineData("holderName")]
    public async Task ClientIdAndAccountIdInTheBody_AreUnknownFields(string field)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var before = await _data.CountAccountsAsync();

        var response = await client.PostAccountAsync(
            $"{{\"holderDocument\":\"123.456.789-09\",\"currency\":\"BRL\",\"{field}\":\"x\"}}");

        response.ShouldBeValidationProblem().ShouldBe([(field, "UNKNOWN_FIELD")]);
        (await _data.CountAccountsAsync()).ShouldBe(before);
    }

    [DockerFact]
    public async Task RejectedRequests_LeaveNoAccountAndNoAuditRow()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accounts = await _data.CountAccountsAsync();
        var audits = await _data.CountAuditAsync("account.created");

        (await client.PostAccountAsync("{}")).StatusCode.ShouldBe(400);
        (await client.PostAccountAsync(null, new WriteRequestOptions { Anonymous = true })).StatusCode.ShouldBe(401);
        (await client.PostAccountAsync(null, new WriteRequestOptions { Token = TestTokenFactory.Create(scope: "ledger.read") }))
            .StatusCode.ShouldBe(403);

        (await _data.CountAccountsAsync()).ShouldBe(accounts);
        (await _data.CountAuditAsync("account.created")).ShouldBe(audits);
    }
}
