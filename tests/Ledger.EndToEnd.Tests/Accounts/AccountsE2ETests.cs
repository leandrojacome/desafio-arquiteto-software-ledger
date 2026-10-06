using System.Text;
using System.Text.RegularExpressions;
using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Accounts;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed partial class AccountsE2ETests(E2EFixture stack)
{
    [E2EFact]
    public async Task CreatingAnAccount_ReturnsOnlyTheMaskedDocument_AndTheDatabaseKeepsItEncrypted()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var document = E2EApi.NewCpf();

        var created = await api.OpenAccountAsync(document);
        var accountId = created.Text("accountId");
        var balance = await api.BalanceAsync(accountId);
        var statement = await api.StatementAsync(accountId);

        created.StatusCode.ShouldBe(201, created.Body);
        created.Text("currency").ShouldBe("BRL");
        created.Text("overdraftLimit").ShouldBe("0.00");
        MaskedDocument().IsMatch(created.Text("holderDocumentMasked")).ShouldBeTrue(created.Body);
        created.Body.ShouldNotContain(document);

        balance.StatusCode.ShouldBe(200, balance.Body);
        balance.Text("balance").ShouldBe("0.00");
        balance.Json().GetProperty("lastEntryId").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        balance.Body.ShouldNotContain(document);
        statement.Json().GetProperty("items").GetArrayLength().ShouldBe(0);

        var stored = await stack.Stack.Database().EncryptedDocumentAsync(accountId);

        stored.Length.ShouldBeGreaterThan(document.Length);
        Encoding.Latin1.GetString(stored).ShouldNotContain(document);
        Encoding.UTF8.GetString(stored).ShouldNotContain(document);
    }

    [E2EFact]
    public async Task AnInvalidAccountRequest_IsRefusedWith400AndTheFieldName()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);

        var negativeLimit = await api.OpenAccountAsync(E2EApi.NewCpf(), "-1.00");
        var otherCurrency = await api.OpenAccountAsync(E2EApi.NewCpf(), currency: "EUR");
        var badDocument = await api.OpenAccountAsync("123");

        foreach (var (response, field) in new[]
                 {
                     (negativeLimit, "overdraftLimit"),
                     (otherCurrency, "currency"),
                     (badDocument, "holderDocument")
                 })
        {
            response.StatusCode.ShouldBe(400, response.Body);
            response.Code().ShouldBe("VALIDATION_FAILED");
            response.Json().GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe(field);
        }
    }

    [E2EFact]
    public async Task ATokenWithOnlyTheReadScope_CannotCreateAnAccount()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);

        var response = await api.OpenAccountAsync(E2EApi.NewCpf(), token: E2ETokenFactory.Create(E2ETokenFactory.ReadOnly));

        response.StatusCode.ShouldBe(403, response.Body);
        response.Code().ShouldBe("FORBIDDEN");
    }

    [E2EFact]
    public async Task TheOverdraftLimit_LetsTheBalanceGoBelowZeroOnlyUpToTheLimit()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var seeder = new E2EAccountSeeder(api);
        var account = await seeder.SeedAsync("100.00", "50.00");

        var withinLimit = await api.DebitAsync(account.AccountId, "150.00");
        var beyondLimit = await api.DebitAsync(account.AccountId, "0.01");
        var balance = await api.BalanceAsync(account.AccountId);

        withinLimit.StatusCode.ShouldBe(201, withinLimit.Body);
        withinLimit.Text("balanceAfter").ShouldBe("-50.00");
        beyondLimit.StatusCode.ShouldBe(422, beyondLimit.Body);
        beyondLimit.Code().ShouldBe("INSUFFICIENT_FUNDS");
        balance.Text("balance").ShouldBe("-50.00");
        balance.Text("overdraftLimit").ShouldBe("50.00");
    }

    [GeneratedRegex(@"^\*{3}\.\*{3}\.\d{3}-\*{2}$")]
    private static partial Regex MaskedDocument();
}
