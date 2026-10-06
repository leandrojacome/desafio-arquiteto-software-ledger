using System.Globalization;
using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Entries;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class EntriesRulesE2ETests(E2EFixture stack)
{
    [E2EFact]
    public async Task APostWithoutTheIdempotencyKey_IsRefusedWith400AndWritesNothing()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var response = await api.SendAsync(
            HttpMethod.Post,
            $"/v1/accounts/{accountId}/entries",
            """{"type":"CREDIT","amount":"1.00","currency":"BRL"}""");

        response.StatusCode.ShouldBe(400, response.Body);
        response.Code().ShouldBe("IDEMPOTENCY_KEY_REQUIRED");
        (await api.StatementAsync(accountId)).Json().GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [E2EFact]
    public async Task InvalidAmountsAndInstants_AreRefusedWith400AndNothingIsStored()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();
        var tooFar = TimeProvider.System.GetUtcNow().AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        foreach (var amount in new[] { "0.00", "-10.00", "10.001", "abc", "1000000000.00" })
        {
            var response = await api.PostEntryAsync(accountId, "CREDIT", amount);

            response.StatusCode.ShouldBe(400, $"{amount}: {response.Body}");
            response.Code().ShouldBe("VALIDATION_FAILED");
            response.Json().GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe("amount");
        }

        var future = await api.SendAsync(
            HttpMethod.Post,
            $"/v1/accounts/{accountId}/entries",
            $$"""{"type":"CREDIT","amount":"1.00","currency":"BRL","occurredAt":"{{tooFar}}"}""",
            E2EApi.NewKey());

        future.StatusCode.ShouldBe(400, future.Body);
        future.Code().ShouldBe("VALIDATION_FAILED");
        (await api.StatementAsync(accountId)).Json().GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [E2EFact]
    public async Task AnEntryInAnotherCurrency_IsRefusedWith422()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var response = await api.SendAsync(
            HttpMethod.Post,
            $"/v1/accounts/{accountId}/entries",
            """{"type":"CREDIT","amount":"1.00","currency":"EUR"}""",
            E2EApi.NewKey());

        response.StatusCode.ShouldBe(422, response.Body);
        response.Code().ShouldBe("CURRENCY_MISMATCH");
    }

    [E2EFact]
    public async Task ARefusedDebit_DoesNotConsumeTheKey_AndIsEvaluatedAgainAfterACredit()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.FundedAccountAsync("100.00");
        var key = E2EApi.NewKey();

        var refused = await api.DebitAsync(accountId, "100.01", key);
        var credit = await api.CreditAsync(accountId, "50.00");
        var accepted = await api.DebitAsync(accountId, "100.01", key);

        refused.StatusCode.ShouldBe(422, refused.Body);
        refused.Code().ShouldBe("INSUFFICIENT_FUNDS");
        credit.StatusCode.ShouldBe(201, credit.Body);
        accepted.StatusCode.ShouldBe(201, accepted.Body);
        accepted.Header("Idempotent-Replayed").ShouldBeNull();
        accepted.Text("balanceAfter").ShouldBe("49.99");
    }

    [E2EFact]
    public async Task TheSameKeyOnTwoAccounts_AreTwoIndependentOperations()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var first = await api.CreateAccountAsync();
        var second = await api.CreateAccountAsync();
        var key = E2EApi.NewKey();

        var one = await api.CreditAsync(first, "10.00", key);
        var other = await api.CreditAsync(second, "10.00", key);

        one.StatusCode.ShouldBe(201, one.Body);
        other.StatusCode.ShouldBe(201, other.Body);
        other.Header("Idempotent-Replayed").ShouldBeNull();
        other.Text("entryId").ShouldNotBe(one.Text("entryId"));
    }

    [E2EFact]
    public async Task ARepeatedRequest_NeverDuplicatesTheEntry_EvenWhenSentTogether()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.FundedAccountAsync("100.00");
        var key = E2EApi.NewKey();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => api.DebitAsync(accountId, "10.00", key))));

        responses.ShouldAllBe(response => response.StatusCode == 201);
        responses.Select(response => response.Text("entryId")).Distinct().Count().ShouldBe(1);
        responses.Count(response => response.Header("Idempotent-Replayed") == "true").ShouldBeGreaterThanOrEqualTo(7);

        var statement = await api.StatementAsync(accountId);

        statement.Json().GetProperty("items").GetArrayLength().ShouldBe(2);
        (await api.BalanceAsync(accountId)).Text("balance").ShouldBe("90.00");
    }

    [E2EFact]
    public async Task Reversals_FollowTheirRules_AndOnlyTheOriginalCanBeReversedOnce()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();
        var otherAccountId = await api.CreateAccountAsync();
        var credit = await api.CreditAsync(accountId, "300.00");
        var spend = await api.DebitAsync(accountId, "280.00");

        var reversal = await api.ReverseAsync(accountId, spend.Text("entryId"));
        var reversalOfReversal = await api.ReverseAsync(accountId, reversal.Text("entryId"));
        var foreignAccount = await api.ReverseAsync(otherAccountId, credit.Text("entryId"));
        var unknown = await api.ReverseAsync(accountId, Guid.NewGuid().ToString());

        reversal.StatusCode.ShouldBe(201, reversal.Body);
        reversal.Text("type").ShouldBe("CREDIT");
        reversal.Text("balanceAfter").ShouldBe("300.00");
        reversalOfReversal.StatusCode.ShouldBe(422, reversalOfReversal.Body);
        reversalOfReversal.Code().ShouldBe("ENTRY_NOT_REVERSIBLE");
        foreignAccount.StatusCode.ShouldBe(404, foreignAccount.Body);
        foreignAccount.Code().ShouldBe("ENTRY_NOT_FOUND");
        unknown.StatusCode.ShouldBe(404, unknown.Body);
    }

    [E2EFact]
    public async Task ReversingACreditWhoseValueWasSpent_IsRefusedAndTheCreditStaysReversible()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();
        var credit = await api.CreditAsync(accountId, "300.00");
        var spend = await api.DebitAsync(accountId, "280.00");

        var refused = await api.ReverseAsync(accountId, credit.Text("entryId"));
        var undo = await api.ReverseAsync(accountId, spend.Text("entryId"));
        var accepted = await api.ReverseAsync(accountId, credit.Text("entryId"));

        refused.StatusCode.ShouldBe(422, refused.Body);
        refused.Code().ShouldBe("INSUFFICIENT_FUNDS");
        undo.StatusCode.ShouldBe(201, undo.Body);
        accepted.StatusCode.ShouldBe(201, accepted.Body);
        accepted.Text("balanceAfter").ShouldBe("0.00");
    }

    [E2EFact]
    public async Task TextWithAccentsInTheDescription_TravelsUntouchedThroughTheLedger()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();
        const string Description = "Cobrança em duplicidade, conciliação de março";

        var created = await api.SendAsync(
            HttpMethod.Post,
            $"/v1/accounts/{accountId}/entries",
            $$"""{"type":"CREDIT","amount":"5.00","currency":"BRL","description":"{{Description}}"}""",
            E2EApi.NewKey());
        var statement = await api.StatementAsync(accountId);

        created.StatusCode.ShouldBe(201, created.Body);
        created.Text("description").ShouldBe(Description);
        statement.Json().GetProperty("items")[0].GetProperty("description").GetString().ShouldBe(Description);
    }
}
