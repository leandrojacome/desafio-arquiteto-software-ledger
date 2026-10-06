using System.Globalization;
using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Reads;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class BalanceInstantE2ETests(E2EFixture stack)
{
    private const string InstantFormat = "yyyy-MM-ddTHH:mm:ss.ffffffZ";

    [E2EFact]
    public async Task TheBalanceAtAPastInstant_IsTheOneOfTheLastEntryRecordedUntilThen()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var first = await api.CreditAsync(accountId, "500.00");
        await Task.Delay(TimeSpan.FromMilliseconds(120), CancellationToken.None);
        var second = await api.DebitAsync(accountId, "150.00");

        var beforeAnything = await api.BalanceAsync(accountId, "2020-01-01T00:00:00Z");
        var atFirst = await api.BalanceAsync(accountId, first.Text("recordedAt"));
        var justBeforeSecond = await api.BalanceAsync(accountId, Shift(second.Text("recordedAt"), TimeSpan.FromMilliseconds(-1)));
        var atSecond = await api.BalanceAsync(accountId, second.Text("recordedAt"));

        beforeAnything.StatusCode.ShouldBe(200, beforeAnything.Body);
        beforeAnything.Text("balance").ShouldBe("0.00");
        beforeAnything.Json().GetProperty("lastEntryId").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        atFirst.Text("balance").ShouldBe("500.00");
        atFirst.Text("lastEntryId").ShouldBe(first.Text("entryId"));
        justBeforeSecond.Text("balance").ShouldBe("500.00");
        atSecond.Text("balance").ShouldBe("350.00");
        atSecond.Text("lastEntryId").ShouldBe(second.Text("entryId"));
    }

    [E2EFact]
    public async Task AFutureInstantAndAnInstantWithoutTimeZone_AreRefused()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.FundedAccountAsync("10.00");
        var future = TimeProvider.System.GetUtcNow().AddHours(1).ToString(InstantFormat, CultureInfo.InvariantCulture);
        var futureInBrasilia = TimeProvider.System.GetUtcNow().AddHours(1).ToOffset(TimeSpan.FromHours(-3))
            .ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);

        var ahead = await api.BalanceAsync(accountId, future);
        var aheadInBrasilia = await api.BalanceAsync(accountId, futureInBrasilia);
        var withoutZone = await api.BalanceAsync(accountId, "2026-10-01T10:00:00");
        var invalidOffset = await api.BalanceAsync(accountId, "2026-10-01T10:00:00+25:00");

        foreach (var response in new[] { ahead, aheadInBrasilia, withoutZone, invalidOffset })
        {
            response.StatusCode.ShouldBe(400, response.Body);
            response.Code().ShouldBe("INVALID_AS_OF");
        }
    }

    [E2EFact]
    public async Task AnInstantInTheBrasiliaOffset_AnswersTheSameBalanceAsItsUtcEquivalent()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var first = await api.CreditAsync(accountId, "500.00");
        await Task.Delay(TimeSpan.FromMilliseconds(120), CancellationToken.None);
        var second = await api.DebitAsync(accountId, "150.00");

        var inUtc = await api.BalanceAsync(accountId, second.Text("recordedAt"));
        var inBrasilia = await api.BalanceAsync(accountId, InBrasilia(second.Text("recordedAt")));
        var beforeInBrasilia = await api.BalanceAsync(accountId, InBrasilia(Shift(second.Text("recordedAt"), TimeSpan.FromMilliseconds(-1))));
        var atFirstInBrasilia = await api.BalanceAsync(accountId, InBrasilia(first.Text("recordedAt")));

        inUtc.StatusCode.ShouldBe(200, inUtc.Body);
        inBrasilia.StatusCode.ShouldBe(200, inBrasilia.Body);
        inBrasilia.Text("balance").ShouldBe("350.00");
        inBrasilia.Text("asOf").ShouldBe(inUtc.Text("asOf"));
        inBrasilia.Text("lastEntryId").ShouldBe(second.Text("entryId"));
        beforeInBrasilia.Text("balance").ShouldBe("500.00");
        atFirstInBrasilia.Text("balance").ShouldBe("500.00");
    }

    [E2EFact]
    public async Task AnInstantOlderThanTheSettlingWindow_IsSettledAndRepeatsTheSameAnswerAfterNewWrites()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();

        var first = await api.CreditAsync(accountId, "100.00");
        await Task.Delay(TimeSpan.FromMilliseconds(120), CancellationToken.None);
        var second = await api.CreditAsync(accountId, "25.50");

        var recent = await api.BalanceAsync(accountId, second.Text("recordedAt"));

        recent.Json().GetProperty("settled").GetBoolean().ShouldBeFalse(recent.Body);

        await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);

        var settled = await api.BalanceAsync(accountId, second.Text("recordedAt"));
        await api.CreditAsync(accountId, "1.00");
        var repeated = await api.BalanceAsync(accountId, second.Text("recordedAt"));
        var earlier = await api.BalanceAsync(accountId, first.Text("recordedAt"));

        settled.Json().GetProperty("settled").GetBoolean().ShouldBeTrue(settled.Body);
        settled.Text("balance").ShouldBe("125.50");
        repeated.Text("balance").ShouldBe("125.50");
        repeated.Text("lastEntryId").ShouldBe(second.Text("entryId"));
        earlier.Text("balance").ShouldBe("100.00");
        (await api.BalanceAsync(accountId)).Text("balance").ShouldBe("126.50");
    }

    private static string InBrasilia(string instant) =>
        DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture).ToOffset(TimeSpan.FromHours(-3))
            .ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);

    private static string Shift(string instant, TimeSpan offset) =>
        DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture).ToUniversalTime().Add(offset)
            .ToString(InstantFormat, CultureInfo.InvariantCulture);
}
