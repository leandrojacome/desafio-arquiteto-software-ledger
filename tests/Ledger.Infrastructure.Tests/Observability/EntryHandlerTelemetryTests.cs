using System.Diagnostics;
using Ledger.Application.Abstractions;
using Ledger.Application.Entries;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Tests.Observability.Support;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class EntryHandlerTelemetryTests : IDisposable
{
    private readonly TestTelemetry _telemetry = new();
    private readonly Activity _request;

    public EntryHandlerTelemetryTests()
    {
        _request = TestActivities.Start("http.request");
        _request.SetTag(ActivityTags.ClientId, EntryFixtures.ClientId);
    }

    public void Dispose()
    {
        _request.Dispose();
        _telemetry.Dispose();
    }

    private RegisterEntryHandler Register(WriteHarness harness) =>
        new(harness.UnitOfWork, harness.Ids, _telemetry.Entries, harness.RegisterLogger);

    private ReverseEntryHandler Reverse(WriteHarness harness) =>
        new(harness.UnitOfWork, harness.Ids, _telemetry.Entries, harness.ReverseLogger);

    private static IdempotencyRecord StoredRecord(RegisterEntryCommand command) =>
        new(CanonicalRequestHash.ForRegistration(command), CanonicalRequestHash.CurrentVersion, EntryFixtures.StoredView());

    [Theory]
    [InlineData(EntryType.Credit, "credit")]
    [InlineData(EntryType.Debit, "debit")]
    public async Task Register_Accepted_CountsTheEntryOnceWithTheClientOfTheRequest(EntryType type, string label)
    {
        var harness = new WriteHarness();

        await Register(harness).HandleAsync(EntryFixtures.RegisterCommand(type), CancellationToken.None);

        _telemetry.Capture.Sum("ledger.entries.recorded", ("type", label), ("client", EntryFixtures.ClientId))
            .ShouldBe(1);
        _telemetry.Capture.Count("ledger.entry.duration", ("type", label), ("outcome", "recorded")).ShouldBe(1);
        _telemetry.Capture.SingleActivity("ledger.record_entry").GetTagItem("ledger.outcome").ShouldBe("recorded");
    }

    [Fact]
    public async Task Register_WhenTheTransactionFunctionRunsTwice_StillCountsTheEntryOnce()
    {
        var harness = new WriteHarness(runs: 2);

        await Register(harness).HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        harness.UnitOfWork.Executions.ShouldBe(2);
        _telemetry.Capture.Of("ledger.entries.recorded").ShouldHaveSingleItem();
        _telemetry.Capture.Of("ledger.entry.duration").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Register_Replayed_CountsTheReplayAndNoRecordedEntry()
    {
        var harness = new WriteHarness();
        var command = EntryFixtures.RegisterCommand();
        harness.ReserveReturns(false);
        harness.FindReturns(StoredRecord(command));

        await Register(harness).HandleAsync(command, CancellationToken.None);

        _telemetry.Capture.Count("idempotency.replays", ("client", EntryFixtures.ClientId)).ShouldBe(1);
        _telemetry.Capture.Of("ledger.entries.recorded").ShouldBeEmpty();
        _telemetry.Capture.SingleActivity("ledger.record_entry").GetTagItem("ledger.idempotent_replay").ShouldBe(true);
    }

    [Fact]
    public async Task Register_KeyReusedWithAnotherRequest_CountsTheConflictAndTheRejection()
    {
        var harness = new WriteHarness();
        var original = EntryFixtures.RegisterCommand(amount: 10m);
        harness.ReserveReturns(false);
        harness.FindReturns(StoredRecord(original));

        await Register(harness).HandleAsync(EntryFixtures.RegisterCommand(amount: 99m), CancellationToken.None);

        _telemetry.Capture.Count("idempotency.conflicts", ("client", EntryFixtures.ClientId)).ShouldBe(1);
        _telemetry.Capture.Count("ledger.entries.rejected", ("reason", "idempotency_conflict")).ShouldBe(1);
    }

    [Fact]
    public async Task Register_AccountMissing_CountsAccountNotFound()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(null);

        await Register(harness).HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        _telemetry.Capture.Count("ledger.entries.rejected", ("reason", "account_not_found")).ShouldBe(1);
    }

    [Fact]
    public async Task Register_CurrencyMismatch_CountsCurrencyMismatch()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(1000m, currency: "EUR"));

        await Register(harness).HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        _telemetry.Capture.Count("ledger.entries.rejected", ("reason", "currency_mismatch")).ShouldBe(1);
    }

    [Fact]
    public async Task Register_InsufficientFunds_CountsInsufficientFundsWithTheClient()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(10m));

        await Register(harness).HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        _telemetry.Capture
            .Count("ledger.entries.rejected", ("reason", "insufficient_funds"), ("client", EntryFixtures.ClientId))
            .ShouldBe(1);
        _telemetry.Capture.Count("ledger.entry.duration", ("outcome", "rejected")).ShouldBe(1);
    }

    [Fact]
    public async Task Register_WithTheInstantMovedAhead_CountsTheCorrection()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(Result.Success(new AppliedEntry(EntryFixtures.StoredView(), true)));

        await Register(harness).HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        _telemetry.Capture.Count("ledger.recorded_at.corrections", ("client", EntryFixtures.ClientId)).ShouldBe(1);
    }

    [Fact]
    public async Task Register_WhenInfrastructureThrows_ReportsFailedAndLetsTheExceptionThrough()
    {
        var harness = new WriteHarness();
        harness.Scope.Entries.TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is gone"));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            Register(harness).HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None));

        _telemetry.Capture.Count("ledger.entry.duration", ("type", "debit"), ("outcome", "failed")).ShouldBe(1);
        _telemetry.Capture.SingleActivity("ledger.record_entry").Status.ShouldBe(ActivityStatusCode.Error);
        _telemetry.Capture.Of("ledger.entries.recorded").ShouldBeEmpty();
    }

    [Fact]
    public async Task Reverse_Accepted_CountsAReversal()
    {
        var harness = new WriteHarness();
        harness.OriginalReturns(EntryFixtures.Candidate());

        await Reverse(harness).HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        _telemetry.Capture.Count("ledger.entries.recorded", ("type", "reversal"), ("client", EntryFixtures.ClientId))
            .ShouldBe(1);
        _telemetry.Capture.SingleActivity("ledger.record_entry").GetTagItem("ledger.entry.type").ShouldBe("reversal");
    }

    [Fact]
    public async Task Reverse_OriginalNotFound_CountsEntryNotFound()
    {
        var harness = new WriteHarness();
        harness.OriginalReturns(null);

        await Reverse(harness).HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        _telemetry.Capture.Count("ledger.entries.rejected", ("reason", "entry_not_found")).ShouldBe(1);
    }

    [Fact]
    public async Task Reverse_OriginalAlreadyReversed_CountsEntryAlreadyReversed()
    {
        var harness = new WriteHarness();
        harness.OriginalReturns(EntryFixtures.Candidate(reversalId: EntryFixtures.Entry));

        await Reverse(harness).HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        _telemetry.Capture.Count("ledger.entries.rejected", ("reason", "entry_already_reversed")).ShouldBe(1);
    }

    [Fact]
    public async Task Reverse_OriginalThatIsItselfAReversal_CountsEntryNotReversible()
    {
        var harness = new WriteHarness();
        harness.OriginalReturns(EntryFixtures.Candidate(reversesEntryId: EntryFixtures.Entry));

        await Reverse(harness).HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        _telemetry.Capture.Count("ledger.entries.rejected", ("reason", "entry_not_reversible")).ShouldBe(1);
    }
}
