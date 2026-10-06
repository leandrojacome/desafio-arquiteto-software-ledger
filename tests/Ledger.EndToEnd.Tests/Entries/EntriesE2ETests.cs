using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Entries;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class EntriesE2ETests(E2EFixture stack)
{
    [E2EFact]
    public async Task TheLifeOfAnAccount_ThroughTheRealStack_FollowsTheWriteContract()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var credit = await api.CreditAsync(accountId, "1000.00");
        var debitKey = E2EApi.NewKey();
        var debit = await api.DebitAsync(accountId, "80.00", debitKey);
        var replay = await api.DebitAsync(accountId, "80.00", debitKey);
        var reused = await api.DebitAsync(accountId, "90.00", debitKey);
        var overdrawn = await api.DebitAsync(accountId, "5000.00");
        var reversal = await api.ReverseAsync(accountId, debit.Text("entryId"));
        var secondReversal = await api.ReverseAsync(accountId, debit.Text("entryId"));

        credit.StatusCode.ShouldBe(201, credit.Body);
        credit.Text("balanceAfter").ShouldBe("1000.00");
        credit.Json().GetProperty("accountVersion").GetInt64().ShouldBe(1);

        debit.StatusCode.ShouldBe(201, debit.Body);
        debit.Text("balanceAfter").ShouldBe("920.00");
        debit.Header("Idempotent-Replayed").ShouldBeNull();

        replay.StatusCode.ShouldBe(201, replay.Body);
        replay.Header("Idempotent-Replayed").ShouldBe("true");
        replay.Text("entryId").ShouldBe(debit.Text("entryId"));
        replay.Text("balanceAfter").ShouldBe("920.00");

        reused.StatusCode.ShouldBe(422, reused.Body);
        reused.Code().ShouldBe("IDEMPOTENCY_KEY_REUSED");

        overdrawn.StatusCode.ShouldBe(422, overdrawn.Body);
        overdrawn.Code().ShouldBe("INSUFFICIENT_FUNDS");
        overdrawn.Body.ShouldNotContain("920.00");

        reversal.StatusCode.ShouldBe(201, reversal.Body);
        reversal.Text("type").ShouldBe("CREDIT");
        reversal.Text("reversesEntryId").ShouldBe(debit.Text("entryId"));
        reversal.Text("balanceAfter").ShouldBe("1000.00");

        secondReversal.StatusCode.ShouldBe(409, secondReversal.Body);
        secondReversal.Code().ShouldBe("ENTRY_ALREADY_REVERSED");
    }

    [E2EFact]
    public async Task TheBalanceAfterOfEachResponse_ClosesTheChain()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();
        var observed = new List<decimal>();

        foreach (var (type, amount) in new[] { ("CREDIT", "500.00"), ("DEBIT", "120.50"), ("CREDIT", "20.25"), ("DEBIT", "99.75") })
        {
            var response = await api.PostEntryAsync(accountId, type, amount);

            response.StatusCode.ShouldBe(201, response.Body);
            observed.Add(decimal.Parse(response.Text("balanceAfter"), System.Globalization.CultureInfo.InvariantCulture));
        }

        observed.ShouldBe([500.00m, 379.50m, 399.75m, 300.00m]);
    }

    [E2EFact]
    public async Task ARequestWithoutToken_IsRefusedWith401()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var response = await api.PostEntryAsync(accountId, "CREDIT", "1.00", anonymous: true);

        response.StatusCode.ShouldBe(401, response.Body);
        response.Code().ShouldBe("UNAUTHENTICATED");
    }

    [E2EFact]
    public async Task ATokenWithOnlyTheReadScope_IsRefusedWith403OnTheWriteRoute()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var response = await api.CreditAsync(accountId, "1.00", token: E2ETokenFactory.Create(E2ETokenFactory.ReadOnly));

        response.StatusCode.ShouldBe(403, response.Body);
        response.Code().ShouldBe("FORBIDDEN");
        response.Header("WWW-Authenticate").ShouldBe("Bearer error=\"insufficient_scope\", scope=\"ledger.write\"");
    }

    [E2EFact]
    public async Task AnAccountThatDoesNotExist_Answers404WithoutRevealingAnything()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);

        var accountId = Guid.NewGuid().ToString();

        var response = await api.CreditAsync(accountId, "1.00");

        response.StatusCode.ShouldBe(404, response.Body);
        response.Code().ShouldBe("ACCOUNT_NOT_FOUND");

        foreach (var property in response.Json().EnumerateObject().Where(property => property.Name != "instance"))
        {
            property.Value.GetRawText().ShouldNotContain(accountId, Case.Insensitive, $"the {property.Name} of the problem repeats the id");
        }

        foreach (var forbidden in new[] { "holder", "document", "balance", "overdraft", "currency" })
        {
            response.Body.ShouldNotContain(forbidden, Case.Insensitive);
        }
    }
}
