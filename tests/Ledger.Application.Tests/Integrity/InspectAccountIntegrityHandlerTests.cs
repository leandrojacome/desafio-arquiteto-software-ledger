using Ledger.Application.Integrity;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Tests.Integrity;

[Trait("Category", "Unit")]
public sealed class InspectAccountIntegrityHandlerTests
{
    private static readonly AccountId Account = AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;

    private FakeIntegritySession Session { get; } = new();

    private InspectAccountIntegrityHandler Handler => new(new FakeIntegritySessions(Session));

    [Fact]
    public async Task HandleAsync_ReturnsTheReportOfTheSessionForTheAccountAsked()
    {
        var finding = new IntegrityFinding(IntegrityCheck.SumBalance, Account, null, null, "10.00", "11.00");
        Session.InspectionFor = account => new AccountIntegrityReport(account, 11.00m, 10.00m, 4, [finding]);

        var report = await Handler.HandleAsync(new InspectAccountIntegrityCommand(Account), CancellationToken.None);

        report.AccountId.ShouldBe(Account);
        report.EntryCount.ShouldBe(4);
        report.Findings.ShouldBe([finding]);
        Session.Inspected.ShouldBe([Account]);
    }

    [Fact]
    public async Task HandleAsync_DisposesTheSessionEvenWhenTheInspectionFails()
    {
        Session.InspectionFor = _ => throw new TimeoutException("db");

        await Should.ThrowAsync<TimeoutException>(
            () => Handler.HandleAsync(new InspectAccountIntegrityCommand(Account), CancellationToken.None));

        Session.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_NeverTakesTheRunLockNorWritesAnything()
    {
        var sessions = new FakeIntegritySessions(Session);

        await new InspectAccountIntegrityHandler(sessions)
            .HandleAsync(new InspectAccountIntegrityCommand(Account), CancellationToken.None);

        sessions.Attempts.ShouldBeEmpty();
        Session.RunRecords.ShouldBeEmpty();
        Session.Violations.ShouldBeEmpty();
    }
}
