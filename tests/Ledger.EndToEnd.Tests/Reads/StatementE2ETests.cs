using System.Globalization;
using System.Text.Json;
using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Reads;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class StatementE2ETests(E2EFixture stack)
{
    private const int Entries = 120;

    [E2EFact]
    public async Task AStatementOfOneHundredAndTwentyEntries_ComesInPagesOf50And50And20WithoutGapsOrRepeats()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();
        var created = await CreditManyAsync(api, accountId, Entries);
        var expected = created.Select(entry => entry.Text("entryId")).Reverse().ToList();

        var firstPage = await api.StatementAsync(accountId, "limit=50");
        var cursor = firstPage.Text("nextCursor");

        var arrivedMeanwhile = await api.CreditAsync(accountId, "1.00");

        var secondPage = await api.StatementAsync(accountId, $"limit=50&cursor={Uri.EscapeDataString(cursor)}");
        var thirdPage = await api.StatementAsync(accountId, $"limit=50&cursor={Uri.EscapeDataString(secondPage.Text("nextCursor"))}");

        arrivedMeanwhile.StatusCode.ShouldBe(201, arrivedMeanwhile.Body);

        var pages = new[] { firstPage, secondPage, thirdPage };

        pages.Select(page => page.Json().GetProperty("items").GetArrayLength()).ShouldBe([50, 50, 20]);
        thirdPage.Json().GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);

        var seen = pages
            .SelectMany(page => page.Json().GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("entryId").GetString() ?? string.Empty)
            .ToList();

        seen.ShouldBe(expected);
        seen.Distinct().Count().ShouldBe(Entries);
    }

    [E2EFact]
    public async Task ThePeriodFilter_IncludesFromAndExcludesTo()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();
        var created = await CreditManyAsync(api, accountId, 12);

        var from = created[3].Text("recordedAt");
        var to = created[8].Text("recordedAt");
        var response = await api.StatementAsync(
            accountId,
            $"from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&limit=200");

        response.StatusCode.ShouldBe(200, response.Body);

        var ids = response.Json().GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("entryId").GetString() ?? string.Empty)
            .ToList();

        ids.ShouldBe(created.Skip(3).Take(5).Select(entry => entry.Text("entryId")).Reverse().ToList());
    }

    [E2EFact]
    public async Task InvalidStatementParameters_AreRefusedWith400()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.FundedAccountAsync("10.00");
        await api.CreditAsync(accountId, "1.00");
        var validCursor = (await api.StatementAsync(accountId, "limit=1")).Text("nextCursor");
        var otherAccount = await api.FundedAccountAsync("10.00");
        var tampered = validCursor[..^2] + (validCursor.EndsWith("AA", StringComparison.Ordinal) ? "BB" : "AA");

        var requests = new Dictionary<string, E2EResponse>
        {
            ["limit=0"] = await api.StatementAsync(accountId, "limit=0"),
            ["limit=201"] = await api.StatementAsync(accountId, "limit=201"),
            ["from after to"] = await api.StatementAsync(accountId, "from=2026-10-04T00:00:00Z&to=2026-10-03T00:00:00Z"),
            ["tampered cursor"] = await api.StatementAsync(accountId, $"cursor={Uri.EscapeDataString(tampered)}"),
            ["cursor of another account"] = await api.StatementAsync(otherAccount, $"cursor={Uri.EscapeDataString(validCursor)}")
        };

        foreach (var (name, response) in requests)
        {
            response.StatusCode.ShouldBe(400, $"{name}: {response.Body}");
            response.Code().ShouldBe("VALIDATION_FAILED");
        }
    }

    [E2EFact]
    public async Task TheStatementOfAnUnknownAccount_Answers404()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);

        var response = await api.StatementAsync(Guid.NewGuid().ToString());
        var balance = await api.BalanceAsync(Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(404, response.Body);
        response.Code().ShouldBe("ACCOUNT_NOT_FOUND");
        balance.StatusCode.ShouldBe(404, balance.Body);
        balance.Code().ShouldBe("ACCOUNT_NOT_FOUND");
    }

    private static async Task<List<E2EResponse>> CreditManyAsync(E2EApi api, string accountId, int count)
    {
        var created = new List<E2EResponse>(count);

        for (var index = 0; index < count; index++)
        {
            var amount = (1 + (index % 7)).ToString("0.00", CultureInfo.InvariantCulture);
            var response = await api.CreditPacedAsync(accountId, amount);

            response.StatusCode.ShouldBe(201, response.Body);
            created.Add(response);
        }

        return created;
    }
}
