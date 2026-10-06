using System.Globalization;
using Ledger.EndToEnd.Tests.Support;
using Xunit.Abstractions;

namespace Ledger.EndToEnd.Tests.Integrity;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class IntegrityE2ETests(E2EFixture stack, ITestOutputHelper output)
{
    private static readonly TimeSpan RunWithin = TimeSpan.FromMinutes(4);

    [E2EFact]
    public async Task TheWorkerRecordsACleanRunForNewEntries_ThenFlagsATamperedBalanceAndCorrectsNothing()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var worker = stack.Stack.Database();
        var admin = stack.Stack.AdminDatabase();

        var clean = await api.CreateAccountAsync();
        var first = await api.CreditAsync(clean, "10.00");
        var second = await api.DebitAsync(clean, "3.00");
        var third = await api.CreditAsync(clean, "2.00");

        first.StatusCode.ShouldBe(201, first.Body);
        second.StatusCode.ShouldBe(201, second.Body);
        third.StatusCode.ShouldBe(201, third.Body);

        var firstAt = Instant(first);
        var lastAt = Instant(third);
        IntegrityRun? covering = null;

        await E2EWait.UntilAsync(
            async () =>
            {
                var runs = await worker.IntegrityRunsAsync(lastAt);

                covering = runs.FirstOrDefault(run =>
                    !run.Partial && run.Mode == "RECENT" && run.WindowStart <= firstAt && run.WindowEnd > lastAt);

                return covering is not null;
            },
            RunWithin,
            "The worker did not record a recent integrity run that covers the new entries",
            TimeSpan.FromSeconds(2));

        var run = covering.ShouldNotBeNull();

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Run recorded with outcome {run.Outcome}, window {run.WindowStart:O} to {run.WindowEnd:O} and {run.Violations} violations."));
        run.Outcome.ShouldBe("SUCCESS");
        run.Violations.ShouldBe(0);
        (await admin.IntegrityViolationsAsync(clean)).ShouldBeEmpty();

        var tampered = await api.CreateAccountAsync();
        var credit = await api.CreditAsync(tampered, "20.00");
        var debit = await api.DebitAsync(tampered, "5.00");

        credit.StatusCode.ShouldBe(201, credit.Body);
        debit.StatusCode.ShouldBe(201, debit.Body);

        await admin.ShiftStoredBalanceAsync(tampered, 1.00m);

        try
        {
            IReadOnlyList<IntegrityViolation> found = [];

            await E2EWait.UntilAsync(
                async () =>
                {
                    found = await admin.IntegrityViolationsAsync(tampered);

                    return found.Count > 0;
                },
                RunWithin,
                "The worker did not flag the tampered stored balance",
                TimeSpan.FromSeconds(2));

            var violation = found[0];

            violation.Check.ShouldBe("HEAD_BALANCE");
            violation.Expected.ShouldBe("15.00");
            violation.Found.ShouldBe("16.00");
            (await admin.StoredBalanceAsync(tampered)).ShouldBe(16.00m, "the worker must report the divergence and never correct it");
            (await admin.CountEntriesAsync(tampered)).ShouldBe(2);
            (await admin.InvariantViolationsAsync(tampered)).ShouldHaveSingleItem()
                .ShouldContain("account_balances holds 16");
            (await admin.IntegrityViolationsAsync(clean)).ShouldBeEmpty();
        }
        finally
        {
            await admin.ShiftStoredBalanceAsync(tampered, -1.00m);
        }

        (await admin.InvariantViolationsAsync(tampered)).ShouldBeEmpty();
    }

    private static DateTimeOffset Instant(E2EResponse response) =>
        DateTimeOffset.Parse(response.Text("recordedAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
