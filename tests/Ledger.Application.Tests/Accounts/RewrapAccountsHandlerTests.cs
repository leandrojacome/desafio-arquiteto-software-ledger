using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Security;
using Ledger.Application.Tests.Security;
using Ledger.Application.Tests.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Application.Tests.Accounts;

[Trait("Category", "Unit")]
public sealed class RewrapAccountsHandlerTests
{
    private const string PassId = "a1b2c3d4e5f60718293a4b5c6d7e8f90";

    private IAccountKeyRewrapper Rewrapper { get; } = Substitute.For<IAccountKeyRewrapper>();

    private IHolderDocumentProtector Protector { get; } = Substitute.For<IHolderDocumentProtector>();

    private ISecurityTelemetry Telemetry { get; } = Substitute.For<ISecurityTelemetry>();

    private IRewrapBatchOperation Operation { get; } = Substitute.For<IRewrapBatchOperation>();

    private IAsyncDisposable Pass { get; } = Substitute.For<IAsyncDisposable>();

    private FakeKeyProvider Keys { get; } = new(2, SecurityVectors.KeySetOne, SecurityVectors.KeySetTwo);

    private CapturingLogger<RewrapAccountsHandler> Logger { get; } = new();

    private RewrapAccountsHandler Handler => new(Rewrapper, Protector, Keys, Telemetry, Logger);

    public RewrapAccountsHandlerTests()
    {
        Telemetry.BeginRewrapBatch().Returns(Operation);
        Rewrapper.TryBeginPassAsync(Arg.Any<CancellationToken>()).Returns(Pass);
        UsageIs();
    }

    private void UsageIs(params (int Version, long Accounts)[] versions) =>
        Rewrapper.ReadKeyUsageAsync(Arg.Any<CancellationToken>())
            .Returns(new KeyUsage(versions.ToDictionary(item => item.Version, item => item.Accounts)));

    private static AccountId Account(int number) =>
        AccountId.From(new Guid(number, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1])).Value;

    private static RewrapAccountsCommand Command(int batchSize = 500) => new(2, batchSize, PassId);

    private void BatchesReturn(params RewrapBatchResult[] batches) =>
        Rewrapper.RewrapBatchAsync(
                Arg.Any<RewrapBatchRequest>(),
                Arg.Any<Func<RewrapRow, Result<ProtectedHolderDocument>>>(),
                Arg.Any<CancellationToken>())
            .Returns(batches[0], batches[1..]);

    private IEnumerable<RewrapBatchRequest> Requests() =>
        Rewrapper.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAccountKeyRewrapper.RewrapBatchAsync))
            .Select(call => (RewrapBatchRequest)call.GetArguments()[0]!);

    [Fact]
    public async Task HandleAsync_AdvancesByTheLastIdOfEachBatchUntilAnEmptyBatch()
    {
        BatchesReturn(
            new RewrapBatchResult(500, 500, 0, Account(500)),
            new RewrapBatchResult(120, 120, 0, Account(620)),
            new RewrapBatchResult(0, 0, 0, null));

        var result = await Handler.HandleAsync(Command(), CancellationToken.None);

        Requests().Select(request => request.AfterId).ShouldBe([null, Account(500), Account(620)]);
        Requests().ShouldAllBe(request =>
            request.ActiveVersion == 2 && request.BatchSize == 500 && request.PassId == PassId);
        result.Rewrapped.ShouldBe(620);
        result.Failed.ShouldBe(0);
    }

    [Fact]
    public async Task HandleAsync_PassWithoutAnyRow_ConvergesAfterASingleCall()
    {
        BatchesReturn(new RewrapBatchResult(0, 0, 0, null));

        var result = await Handler.HandleAsync(Command(), CancellationToken.None);

        result.Converged.ShouldBeTrue();
        result.Rewrapped.ShouldBe(0);
        await Rewrapper.Received(1).RewrapBatchAsync(
            Arg.Any<RewrapBatchRequest>(),
            Arg.Any<Func<RewrapRow, Result<ProtectedHolderDocument>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AnotherInstanceHoldsThePass_SkipsWithoutSelectingOrReportingConvergence()
    {
        Rewrapper.TryBeginPassAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IAsyncDisposable?>(null));

        var result = await Handler.HandleAsync(Command(), CancellationToken.None);

        result.Skipped.ShouldBeTrue();
        result.Converged.ShouldBeFalse();
        result.Rewrapped.ShouldBe(0);
        await Rewrapper.DidNotReceive().RewrapBatchAsync(
            Arg.Any<RewrapBatchRequest>(),
            Arg.Any<Func<RewrapRow, Result<ProtectedHolderDocument>>>(),
            Arg.Any<CancellationToken>());
        await Rewrapper.DidNotReceive().ReadKeyUsageAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EndOfThePass_ReleasesTheLock()
    {
        BatchesReturn(new RewrapBatchResult(0, 0, 0, null));

        await Handler.HandleAsync(Command(), CancellationToken.None);

        await Pass.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task HandleAsync_RewrapperFails_StillReleasesTheLock()
    {
        Rewrapper.RewrapBatchAsync(
                Arg.Any<RewrapBatchRequest>(),
                Arg.Any<Func<RewrapRow, Result<ProtectedHolderDocument>>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(() => Handler.HandleAsync(Command(), CancellationToken.None));

        await Pass.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task HandleAsync_EmptyPassWhileAccountsRemainBelowTheActiveVersion_DoesNotDeclareConvergence()
    {
        BatchesReturn(new RewrapBatchResult(0, 0, 0, null));
        UsageIs((1, 40), (2, 100));

        var result = await Handler.HandleAsync(Command(), CancellationToken.None);

        result.Rewrapped.ShouldBe(0);
        result.Converged.ShouldBeFalse();
        Telemetry.Received(1).KeyUsageObserved(40, false);
    }

    [Fact]
    public async Task HandleAsync_AccountsOnlyOnTheActiveVersion_ConvergesAndReportsZeroPending()
    {
        BatchesReturn(new RewrapBatchResult(0, 0, 0, null));
        UsageIs((2, 100));

        var result = await Handler.HandleAsync(Command(), CancellationToken.None);

        result.Converged.ShouldBeTrue();
        Telemetry.Received(1).KeyUsageObserved(0, false);
        Logger.Contains(7010).ShouldBeFalse();
        Logger.Contains(7011).ShouldBeFalse();
    }

    [Fact]
    public async Task HandleAsync_AccountsSealedWithAVersionNewerThanTheActiveOne_DoesNotConvergeAndLogsTheError()
    {
        BatchesReturn(new RewrapBatchResult(0, 0, 0, null));
        UsageIs((1, 3), (2, 7));
        var staleWorker = new RewrapAccountsCommand(1, 500, PassId);
        var staleKeys = new FakeKeyProvider(1, SecurityVectors.KeySetOne, SecurityVectors.KeySetTwo);

        var result = await new RewrapAccountsHandler(Rewrapper, Protector, staleKeys, Telemetry, Logger)
            .HandleAsync(staleWorker, CancellationToken.None);

        result.Converged.ShouldBeFalse();
        Telemetry.Received(1).KeyUsageObserved(0, true);
        var log = Logger.Single(7010);
        log.Level.ShouldBe(LogLevel.Error);
        log.Properties["Versions"].ShouldBe("2");
        log.Properties["ActiveVersion"].ShouldBe("1");
    }

    [Fact]
    public async Task HandleAsync_AccountsSealedWithAVersionTheProcessCannotRead_DoesNotConvergeAndLogsTheError()
    {
        BatchesReturn(new RewrapBatchResult(0, 0, 0, null));
        UsageIs((2, 100), (3, 1));
        var onlyOld = new FakeKeyProvider(2, SecurityVectors.KeySetTwo);

        var result = await new RewrapAccountsHandler(Rewrapper, Protector, onlyOld, Telemetry, Logger)
            .HandleAsync(Command(), CancellationToken.None);

        result.Converged.ShouldBeFalse();
        Telemetry.Received(1).KeyUsageObserved(0, true);
        Logger.Single(7011).Properties["Versions"].ShouldBe("3");
    }

    [Fact]
    public async Task HandleAsync_AccountsLeftInARetiredVersion_AreReportedAsPendingAndAsUnreadable()
    {
        BatchesReturn(new RewrapBatchResult(0, 0, 0, null));
        UsageIs((1, 12), (2, 100));
        var withoutVersionOne = new FakeKeyProvider(2, SecurityVectors.KeySetTwo);

        var result = await new RewrapAccountsHandler(Rewrapper, Protector, withoutVersionOne, Telemetry, Logger)
            .HandleAsync(Command(), CancellationToken.None);

        result.Converged.ShouldBeFalse();
        Telemetry.Received(1).KeyUsageObserved(12, true);
        Logger.Single(7011).Properties["Versions"].ShouldBe("1");
    }

    [Fact]
    public async Task HandleAsync_PassThatRewrappedSomething_DoesNotDeclareConvergence()
    {
        BatchesReturn(
            new RewrapBatchResult(10, 10, 0, Account(10)),
            new RewrapBatchResult(0, 0, 0, null));

        var result = await Handler.HandleAsync(Command(), CancellationToken.None);

        result.Converged.ShouldBeFalse();
    }

    [Fact]
    public async Task HandleAsync_PassThatSkippedARow_DoesNotDeclareConvergenceAndKeepsGoing()
    {
        BatchesReturn(
            new RewrapBatchResult(10, 9, 1, Account(10)),
            new RewrapBatchResult(5, 5, 0, Account(15)),
            new RewrapBatchResult(0, 0, 0, null));

        var result = await Handler.HandleAsync(Command(10), CancellationToken.None);

        result.Converged.ShouldBeFalse();
        result.Rewrapped.ShouldBe(14);
        result.Failed.ShouldBe(1);
        Requests().Count().ShouldBe(3);
    }

    [Fact]
    public async Task HandleAsync_EveryBatch_OpensASpanAndCompletesItWithTheCounts()
    {
        BatchesReturn(
            new RewrapBatchResult(10, 9, 1, Account(10)),
            new RewrapBatchResult(0, 0, 0, null));

        await Handler.HandleAsync(Command(), CancellationToken.None);

        Telemetry.Received(2).BeginRewrapBatch();
        Operation.Received(1).Complete(9, 1);
        Operation.Received(1).Complete(0, 0);
        Operation.Received(2).Dispose();
    }

    [Fact]
    public async Task HandleAsync_Counters_GrowByTheDecryptedAndByTheRewrappedAndFailedQuantities()
    {
        BatchesReturn(
            new RewrapBatchResult(10, 9, 1, Account(10)),
            new RewrapBatchResult(4, 4, 0, Account(14)),
            new RewrapBatchResult(0, 0, 0, null));

        await Handler.HandleAsync(Command(), CancellationToken.None);

        Telemetry.Received(1).PiiDecrypted("rewrap", 9);
        Telemetry.Received(1).PiiDecrypted("rewrap", 4);
        Telemetry.Received(1).AccountsRewrapped(9, 1);
        Telemetry.Received(1).AccountsRewrapped(4, 0);
        Telemetry.DidNotReceive().PiiDecrypted(Arg.Any<string>(), 0);
    }

    [Fact]
    public async Task HandleAsync_EveryNonEmptyBatch_LogsTheCompletionAtInformation()
    {
        BatchesReturn(
            new RewrapBatchResult(10, 9, 1, Account(10)),
            new RewrapBatchResult(0, 0, 0, null));

        await Handler.HandleAsync(Command(), CancellationToken.None);

        var log = Logger.Single(7006);
        log.Level.ShouldBe(LogLevel.Information);
        log.Properties["Rewrapped"].ShouldBe("9");
        log.Properties["Failed"].ShouldBe("1");
        log.Properties["ToVersion"].ShouldBe("2");
    }

    [Fact]
    public async Task HandleAsync_TransformFunction_ReprotectsEachRowWithItsOwnAccountId()
    {
        var protectedDocument = new ProtectedHolderDocument(new byte[] { 1, 0, 2 }, new byte[] { 5 }, 2);
        Protector.Reprotect(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<AccountId>()).Returns(protectedDocument);
        Func<RewrapRow, Result<ProtectedHolderDocument>>? transform = null;
        Rewrapper.RewrapBatchAsync(
                Arg.Any<RewrapBatchRequest>(),
                Arg.Do<Func<RewrapRow, Result<ProtectedHolderDocument>>>(captured => transform = captured),
                Arg.Any<CancellationToken>())
            .Returns(new RewrapBatchResult(0, 0, 0, null));

        await Handler.HandleAsync(Command(), CancellationToken.None);

        transform.ShouldNotBeNull();
        var row = new RewrapRow(Account(7), new byte[] { 1, 0, 1, 9 }, 1);
        var transformed = transform(row);
        transformed.Value.ShouldBe(protectedDocument);
        Protector.Received(1).Reprotect(
            Arg.Is<ReadOnlyMemory<byte>>(blob => blob.ToArray().SequenceEqual(new byte[] { 1, 0, 1, 9 })),
            Account(7));
    }

    [Fact]
    public async Task HandleAsync_RowThatCannotBeDecrypted_LogsOnlyTheAccountIdAtError()
    {
        Protector.Reprotect(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<AccountId>())
            .Returns(Result.Failure<ProtectedHolderDocument>(AccountErrors.InvalidHolderDocument));
        Func<RewrapRow, Result<ProtectedHolderDocument>>? transform = null;
        Rewrapper.RewrapBatchAsync(
                Arg.Any<RewrapBatchRequest>(),
                Arg.Do<Func<RewrapRow, Result<ProtectedHolderDocument>>>(captured => transform = captured),
                Arg.Any<CancellationToken>())
            .Returns(new RewrapBatchResult(0, 0, 0, null));
        await Handler.HandleAsync(Command(), CancellationToken.None);
        transform.ShouldNotBeNull();

        var transformed = transform(new RewrapRow(Account(7), new byte[] { 1, 0, 1, 9 }, 1));

        transformed.IsFailure.ShouldBeTrue();
        var log = Logger.Single(7007);
        log.Level.ShouldBe(LogLevel.Error);
        log.Properties["AccountId"].ShouldBe(Account(7).ToString());
        log.Properties.Count.ShouldBe(2);
    }

    [Fact]
    public async Task HandleAsync_NonEmptyBatchWithoutALastId_Throws()
    {
        BatchesReturn(new RewrapBatchResult(3, 3, 0, null));

        await Should.ThrowAsync<InvalidOperationException>(() => Handler.HandleAsync(Command(), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_RewrapperFails_TheExceptionPropagatesAndTheSpanIsDisposed()
    {
        Rewrapper.RewrapBatchAsync(
                Arg.Any<RewrapBatchRequest>(),
                Arg.Any<Func<RewrapRow, Result<ProtectedHolderDocument>>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(() => Handler.HandleAsync(Command(), CancellationToken.None));

        Operation.Received(1).Dispose();
    }

    [Fact]
    public async Task HandleAsync_CanceledToken_ThrowsBeforeTouchingTheRewrapper()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Handler.HandleAsync(Command(), source.Token));

        Rewrapper.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_TokenReachesTheRewrapper()
    {
        BatchesReturn(new RewrapBatchResult(0, 0, 0, null));
        using var source = new CancellationTokenSource();

        await Handler.HandleAsync(Command(), source.Token);

        await Rewrapper.Received(1).RewrapBatchAsync(
            Arg.Any<RewrapBatchRequest>(),
            Arg.Any<Func<RewrapRow, Result<ProtectedHolderDocument>>>(),
            source.Token);
    }

    [Fact]
    public void ToString_OfARow_NeverPrintsTheBlob()
    {
        var row = new RewrapRow(Account(7), new byte[] { 1, 0, 1, 9, 8, 7 }, 1);

        row.ToString().ShouldNotContain("ReadOnlyMemory");
        row.ToString().ShouldContain(Account(7).ToString());
    }
}
