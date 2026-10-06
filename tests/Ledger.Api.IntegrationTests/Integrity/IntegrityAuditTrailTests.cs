using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Integrity;
using Microsoft.Extensions.Logging;

namespace Ledger.Api.IntegrationTests.Integrity;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class IntegrityAuditTrailTests(PostgresFixture postgres)
{
    private const string SentinelExpected = "12345.67";
    private const string SentinelFound = "12353.44";

    [DockerFact]
    public async Task TheRunRecord_CarriesTheModeTheWindowAndTheCounts()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);

        await harness.Ledger.SeedAsync();
        await harness.RunAsync(IntegrityMode.Recent);

        var row = (await harness.Ledger.AuditRowsAsync("integrity.run_completed")).ShouldHaveSingleItem();

        row.ClientId.ShouldBe("ledger-worker");
        row.AccountId.ShouldBeNull();
        row.Outcome.ShouldBe("SUCCESS");
        row.CorrelationId.Length.ShouldBe(32);
        row.CorrelationId.ShouldAllBe(character => char.IsAsciiHexDigitLower(character));

        using var details = JsonDocument.Parse(row.Details);

        var root = details.RootElement;

        root.GetProperty("mode").GetString().ShouldBe("RECENT");
        root.GetProperty("windowStart").GetString().ShouldEndWith("Z");
        root.GetProperty("windowEnd").GetString().ShouldEndWith("Z");
        root.GetProperty("accountsChecked").GetInt64().ShouldBe(1);
        root.GetProperty("entriesChecked").GetInt64().ShouldBe(5);
        root.GetProperty("violations").GetInt32().ShouldBe(0);
        root.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["mode", "windowStart", "windowEnd", "accountsChecked", "entriesChecked", "violations"],
            ignoreOrder: true);
    }

    [DockerFact]
    public async Task TheViolationRecord_SharesTheRunIdWithTheRunRecordAndKeepsTheValuesOnlyInTheTrail()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync([("CREDIT", 12345.67m)], 0m, null);

        await harness.Ledger.TamperStoredBalanceAsync(account.AccountId, 7.77m);
        await harness.RunAsync(IntegrityMode.Recent);

        var violation = (await harness.Ledger.AuditRowsAsync("integrity.violation_detected")).ShouldHaveSingleItem();
        var run = (await harness.Ledger.AuditRowsAsync("integrity.run_completed")).ShouldHaveSingleItem();

        violation.ClientId.ShouldBe("ledger-worker");
        violation.Outcome.ShouldBe("FAILURE");
        violation.AccountId.ShouldBe(account.AccountId);
        violation.CorrelationId.ShouldBe(run.CorrelationId);
        run.Outcome.ShouldBe("FAILURE");

        using var details = JsonDocument.Parse(violation.Details);

        var root = details.RootElement;

        root.GetProperty("runId").GetString().ShouldBe(run.CorrelationId);
        root.GetProperty("mode").GetString().ShouldBe("RECENT");
        root.GetProperty("check").GetString().ShouldBe("HEAD_BALANCE");
        root.GetProperty("expected").GetString().ShouldBe(SentinelExpected);
        root.GetProperty("found").GetString().ShouldBe(SentinelFound);
        root.TryGetProperty("entryId", out _).ShouldBeFalse();
        root.TryGetProperty("accountVersion", out _).ShouldBeFalse();

        foreach (var captured in harness.HandlerLog.Events)
        {
            Rendered(captured).ShouldNotContain(SentinelExpected);
            Rendered(captured).ShouldNotContain(SentinelFound);
            Rendered(captured).ShouldNotContain("7.77");
        }
    }

    [DockerFact]
    public async Task ChainViolations_CarryTheEntryAndTheVersion()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();

        await harness.Ledger.DeleteEntryAsync(account.Entries[2].Id);
        await harness.RunAsync(IntegrityMode.Recent);

        var violation = (await harness.Ledger.AuditRowsAsync("integrity.violation_detected")).ShouldHaveSingleItem();

        using var details = JsonDocument.Parse(violation.Details);

        details.RootElement.GetProperty("entryId").GetString().ShouldBe(account.Entries[3].Id.ToString("D"));
        details.RootElement.GetProperty("accountVersion").GetInt64().ShouldBe(4);
    }

    [DockerFact]
    public async Task TheLogs_HaveEvent4001AsInformationAndEvent4002AsErrorWithoutAnyValue()
    {
        await using var harness = await IntegrityHarness.CreateAsync(postgres);
        var account = await harness.Ledger.SeedAsync();

        await harness.Ledger.TamperStoredBalanceAsync(account.AccountId, 1.00m);
        await harness.RunAsync(IntegrityMode.Recent);

        var completed = harness.HandlerLog.Events.Single(captured => captured.Id == 4001);
        var detected = harness.HandlerLog.Events.Single(captured => captured.Id == 4002);

        completed.Level.ShouldBe(LogLevel.Information);
        completed.Name.ShouldBe("IntegrityRunCompleted");
        detected.Level.ShouldBe(LogLevel.Error);
        detected.Name.ShouldBe("IntegrityViolationDetected");
        detected.Properties["Check"].ShouldBe("HEAD_BALANCE");
        detected.Properties.Keys.ShouldNotContain("Expected");
        detected.Properties.Keys.ShouldNotContain("Found");
        Rendered(detected).ShouldNotContain("111.00");
        Rendered(detected).ShouldNotContain("110.00");
    }

    private static string Rendered(CapturedEvent captured) =>
        captured.Message + " " + string.Join(' ', captured.Properties.Select(pair => $"{pair.Key}={pair.Value}"));
}
