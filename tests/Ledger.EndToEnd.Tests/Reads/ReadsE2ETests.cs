using System.Globalization;
using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Reads;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class ReadsE2ETests(E2EFixture stack)
{
    [E2EFact]
    public async Task TheBalanceAndTheStatement_ThroughTheRealStack_TellTheSameStory()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var credit = await api.CreditAsync(accountId, "1000.00");
        var debit = await api.DebitAsync(accountId, "80.00");

        credit.StatusCode.ShouldBe(201, credit.Body);
        debit.StatusCode.ShouldBe(201, debit.Body);

        var current = await api.BalanceAsync(accountId);
        var atTheCredit = await api.BalanceAsync(accountId, credit.Text("recordedAt"));

        current.StatusCode.ShouldBe(200, current.Body);
        current.Text("balance").ShouldBe("920.00");
        current.Text("currency").ShouldBe("BRL");
        current.Text("lastEntryId").ShouldBe(debit.Text("entryId"));
        current.Header("Cache-Control").ShouldBe("no-store");
        current.Json().TryGetProperty("settled", out _).ShouldBeFalse();

        atTheCredit.StatusCode.ShouldBe(200, atTheCredit.Body);
        atTheCredit.Text("balance").ShouldBe("1000.00");
        atTheCredit.Text("lastEntryId").ShouldBe(credit.Text("entryId"));
        atTheCredit.Json().TryGetProperty("settled", out _).ShouldBeTrue();

        var first = await api.StatementAsync(accountId, "limit=1");

        first.StatusCode.ShouldBe(200, first.Body);

        var firstItems = first.Json().GetProperty("items");
        var cursor = first.Text("nextCursor");

        firstItems.GetArrayLength().ShouldBe(1);
        firstItems[0].GetProperty("entryId").GetString().ShouldBe(debit.Text("entryId"));

        var second = await api.StatementAsync(accountId, $"limit=1&cursor={Uri.EscapeDataString(cursor)}");

        second.StatusCode.ShouldBe(200, second.Body);

        var secondItems = second.Json().GetProperty("items");

        secondItems.GetArrayLength().ShouldBe(1);
        secondItems[0].GetProperty("entryId").GetString().ShouldBe(credit.Text("entryId"));
        second.Json().GetProperty("nextCursor").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        var signedSum = Signed(firstItems[0]) + Signed(secondItems[0]);

        signedSum.ShouldBe(decimal.Parse(current.Text("balance"), CultureInfo.InvariantCulture));
    }

    [E2EFact]
    public async Task AnAsOfWithoutTimeZone_Answers400WithTheAsOfCode()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var response = await api.BalanceAsync(accountId, "2026-10-01T11:03:11");

        response.StatusCode.ShouldBe(400, response.Body);
        response.Code().ShouldBe("INVALID_AS_OF");
    }

    [E2EFact]
    public async Task AnAsOfInTheBrasiliaOffset_IsAcceptedAndAnsweredInUtc()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var response = await api.BalanceAsync(accountId, "2026-10-01T11:03:11-03:00");

        response.StatusCode.ShouldBe(200, response.Body);
        response.Text("asOf").ShouldBe("2026-10-01T14:03:11.000000Z");
        response.Text("balance").ShouldBe("0.00");
    }

    [E2EFact]
    public async Task AnUnknownQueryParameter_Answers400WithTheValidationCode()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var response = await api.StatementAsync(accountId, "foo=1");

        response.StatusCode.ShouldBe(400, response.Body);
        response.Code().ShouldBe("VALIDATION_FAILED");
        response.Json().GetProperty("errors")[0].GetProperty("reason").GetString().ShouldBe("UNKNOWN_FIELD");
    }

    [E2EFact]
    public async Task ARequestWithoutToken_Answers401OnBothReadRoutes()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var balance = await api.BalanceAsync(accountId, anonymous: true);
        var statement = await api.StatementAsync(accountId, anonymous: true);

        balance.StatusCode.ShouldBe(401, balance.Body);
        statement.StatusCode.ShouldBe(401, statement.Body);
    }

    [E2EFact]
    public async Task ATokenWithOnlyTheWriteScope_Answers403OnBothReadRoutes()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();
        var token = E2ETokenFactory.Create(E2ETokenFactory.WriteOnly);

        var balance = await api.BalanceAsync(accountId, token: token);
        var statement = await api.StatementAsync(accountId, token: token);

        balance.StatusCode.ShouldBe(403, balance.Body);
        statement.StatusCode.ShouldBe(403, statement.Body);
        balance.Header("WWW-Authenticate").ShouldBe("Bearer error=\"insufficient_scope\", scope=\"ledger.read\"");
    }

    private static decimal Signed(System.Text.Json.JsonElement item)
    {
        var amount = decimal.Parse(item.GetProperty("amount").GetString() ?? "0", CultureInfo.InvariantCulture);

        return item.GetProperty("type").GetString() == "CREDIT" ? amount : -amount;
    }
}
