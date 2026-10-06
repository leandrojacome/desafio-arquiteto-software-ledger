namespace Ledger.EndToEnd.Tests.Support;

internal sealed record SeededAccount(string AccountId, string Document);

internal sealed class E2EAccountSeeder(E2EApi api)
{
    public async Task<SeededAccount> SeedAsync(string? openingBalance = null, string? overdraftLimit = null)
    {
        var document = E2EApi.NewCpf();
        var created = await api.OpenAccountAsync(document, overdraftLimit);

        created.StatusCode.ShouldBe(201, created.Body);

        var account = new SeededAccount(created.Text("accountId"), document);

        if (openingBalance is not null)
        {
            var funding = await api.CreditAsync(account.AccountId, openingBalance);

            funding.StatusCode.ShouldBe(201, funding.Body);
        }

        return account;
    }

    public async Task<IReadOnlyList<SeededAccount>> SeedManyAsync(int count, string? openingBalance = null)
    {
        var accounts = new List<SeededAccount>(count);

        for (var index = 0; index < count; index++)
        {
            accounts.Add(await SeedAsync(openingBalance));
        }

        return accounts;
    }
}
