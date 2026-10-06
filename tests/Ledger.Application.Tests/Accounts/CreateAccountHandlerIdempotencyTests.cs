using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Audit;
using Ledger.Application.Security;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Application.Tests.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Application.Tests.Accounts;

[Trait("Category", "Unit")]
public sealed class CreateAccountHandlerIdempotencyTests
{
    private const string ClientId = "pix-core";
    private const string CorrelationId = "5d1b7c0e9a3f4c28b6e1d04f7a92c3b8";
    private const string Currency = "BRL";

    private static readonly Guid AccountGuid = Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");
    private static readonly Guid OtherGuid = Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d77");
    private static readonly DateTimeOffset CreatedAt = new DateTimeOffset(2026, 9, 14, 12, 0, 3, TimeSpan.Zero).AddTicks(4_152_060);
    private static readonly DateTimeOffset EarlierCreatedAt = CreatedAt.AddMinutes(-3);
    private static readonly byte[] Index = [7, 7, 7, 7];
    private static readonly byte[] OtherIndex = [8, 8, 8, 8];
    private static readonly IdempotencyKey Key = IdempotencyKey.From("create-account-0001").Value;

    private InlineUnitOfWork UnitOfWork { get; } = new();

    private IHolderDocumentProtector Protector { get; } = Substitute.For<IHolderDocumentProtector>();

    private IIdGenerator Ids { get; } = Substitute.For<IIdGenerator>();

    private ISecurityTelemetry Telemetry { get; } = Substitute.For<ISecurityTelemetry>();

    private IAccountCreationOperation Operation { get; } = Substitute.For<IAccountCreationOperation>();

    private CapturingLogger<CreateAccountHandler> Logger { get; } = new();

    private CreateAccountHandler Handler { get; }

    public CreateAccountHandlerIdempotencyTests()
    {
        Ids.NewId().Returns(AccountGuid);
        Telemetry.BeginCreateAccount().Returns(Operation);
        Protector.Protect(Arg.Any<HolderDocument>(), Arg.Any<AccountId>())
            .Returns(new ProtectedHolderDocument(new byte[] { 1, 0, 1, 9 }, Index, 1));
        Protector.BlindIndexCandidates(Arg.Any<HolderDocument>())
            .Returns([(ReadOnlyMemory<byte>)Index]);
        UnitOfWork.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>()).Returns(CreatedAt);
        UnitOfWork.Scope.Accounts.TryReserveCreationKeyAsync(Arg.Any<AccountCreationKeyReservation>(), Arg.Any<CancellationToken>())
            .Returns(true);
        Handler = new CreateAccountHandler(UnitOfWork, Protector, Ids, Telemetry, Logger);
    }

    private static AccountId Generated => AccountId.From(AccountGuid).Value;

    private static AccountId Other => AccountId.From(OtherGuid).Value;

    private static CreateAccountCommand Command(IdempotencyKey? key, decimal overdraft = 0m) =>
        new(
            HolderDocument.From("123.456.789-09").Value,
            Currency,
            Money.Create(overdraft, Currency).Value,
            ClientId,
            CorrelationId,
            key);

    private static AccountCreationKeyRecord RecordFor(
        byte[] blindIndex,
        decimal overdraft = 0m,
        int version = AccountCreationRequestHash.CurrentVersion) =>
        new(
            AccountCreationRequestHash.Compute(Currency, Money.Create(overdraft, Currency).Value, blindIndex),
            version,
            Other,
            EarlierCreatedAt);

    private void ConflictWith(AccountCreationKeyRecord? record)
    {
        UnitOfWork.Scope.Accounts.TryReserveCreationKeyAsync(Arg.Any<AccountCreationKeyReservation>(), Arg.Any<CancellationToken>())
            .Returns(false);
        UnitOfWork.Scope.Accounts.FindCreationKeyAsync(Arg.Any<string>(), Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>())
            .Returns(record);
    }

    [Fact]
    public async Task HandleAsync_WithAKeyAndNoPriorRecord_ReservesTheKeyThenCreatesTheAccount()
    {
        var result = await Handler.HandleAsync(Command(Key), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReplay.ShouldBeFalse();
        result.Value.AccountId.ShouldBe(Generated);
        result.Value.CreatedAt.ShouldBe(CreatedAt);
        UnitOfWork.Commits.ShouldBe(1);
        Operation.Received(1).Outcome("created");
        Telemetry.Received(1).AccountCreated();
        await UnitOfWork.Scope.Accounts.DidNotReceive()
            .FindCreationKeyAsync(Arg.Any<string>(), Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithAKey_ReservesWithTheClientTheGeneratedIdAndTheHashOfTheBody()
    {
        await Handler.HandleAsync(Command(Key, overdraft: 25m), CancellationToken.None);

        var reservation = UnitOfWork.Scope.Accounts.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IAccountRepository.TryReserveCreationKeyAsync))
            .SelectMany(call => call.GetArguments())
            .OfType<AccountCreationKeyReservation>()
            .Single();

        reservation.ClientId.ShouldBe(ClientId);
        reservation.Key.ShouldBe(Key);
        reservation.AccountId.ShouldBe(Generated);
        reservation.HashVersion.ShouldBe(AccountCreationRequestHash.CurrentVersion);
        reservation.RequestHash.ToArray().ShouldBe(
            AccountCreationRequestHash.Compute(Currency, Money.Create(25m, Currency).Value, Index));
    }

    [Fact]
    public async Task HandleAsync_WithAKey_RecordsTheAuditEventOnlyForTheNewAccount()
    {
        await Handler.HandleAsync(Command(Key), CancellationToken.None);

        await UnitOfWork.Scope.Audit.Received(1).RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithoutAKey_NeverTouchesTheCreationKeys()
    {
        var result = await Handler.HandleAsync(Command(null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReplay.ShouldBeFalse();
        await UnitOfWork.Scope.Accounts.DidNotReceive()
            .TryReserveCreationKeyAsync(Arg.Any<AccountCreationKeyReservation>(), Arg.Any<CancellationToken>());
        await UnitOfWork.Scope.Accounts.DidNotReceive()
            .FindCreationKeyAsync(Arg.Any<string>(), Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>());
        Protector.DidNotReceive().BlindIndexCandidates(Arg.Any<HolderDocument>());
    }

    [Fact]
    public async Task HandleAsync_KeyAlreadyUsedWithTheSameBody_ReturnsTheOriginalAccountAsAReplay()
    {
        ConflictWith(RecordFor(Index));

        var result = await Handler.HandleAsync(Command(Key), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReplay.ShouldBeTrue();
        result.Value.AccountId.ShouldBe(Other);
        result.Value.CreatedAt.ShouldBe(EarlierCreatedAt);
        result.Value.HolderDocumentMasked.ShouldBe("***.***.789-**");
        Operation.Received(1).Outcome("already_created");
    }

    [Fact]
    public async Task HandleAsync_Replay_WritesNothingAndRollsBackTheTransaction()
    {
        ConflictWith(RecordFor(Index));

        await Handler.HandleAsync(Command(Key), CancellationToken.None);

        await UnitOfWork.Scope.Accounts.DidNotReceive().CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>());
        await UnitOfWork.Scope.Audit.DidNotReceive().RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
        UnitOfWork.Commits.ShouldBe(0);
        UnitOfWork.Rollbacks.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_Replay_DoesNotCountOrLogANewAccount()
    {
        ConflictWith(RecordFor(Index));

        await Handler.HandleAsync(Command(Key), CancellationToken.None);

        Telemetry.DidNotReceive().AccountCreated();
        Logger.Entries.Count(entry => entry.EventId.Id == 7001).ShouldBe(0);
    }

    [Fact]
    public async Task HandleAsync_KeyAlreadyUsedWithAnotherOverdraftLimit_IsRefusedAsReused()
    {
        ConflictWith(RecordFor(Index, overdraft: 100m));

        var result = await Handler.HandleAsync(Command(Key, overdraft: 50m), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.CreationKeyReused);
        result.Error.Kind.ShouldBe(ErrorKind.Unprocessable);
        await UnitOfWork.Scope.Accounts.DidNotReceive().CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>());
        Telemetry.DidNotReceive().AccountCreated();
    }

    [Fact]
    public async Task HandleAsync_KeyAlreadyUsedWithAnotherDocument_IsRefusedAsReused()
    {
        ConflictWith(RecordFor(OtherIndex));

        var result = await Handler.HandleAsync(Command(Key), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.CreationKeyReused);
    }

    [Fact]
    public async Task HandleAsync_RecordMadeUnderAnOlderKeyVersion_StillMatchesTheSameDocument()
    {
        Protector.BlindIndexCandidates(Arg.Any<HolderDocument>())
            .Returns([(ReadOnlyMemory<byte>)OtherIndex, (ReadOnlyMemory<byte>)Index]);
        ConflictWith(RecordFor(Index));

        var result = await Handler.HandleAsync(Command(Key), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReplay.ShouldBeTrue();
        result.Value.AccountId.ShouldBe(Other);
    }

    [Fact]
    public async Task HandleAsync_RecordWithAnUnknownHashVersion_Throws()
    {
        ConflictWith(RecordFor(Index, version: AccountCreationRequestHash.CurrentVersion + 1));

        await Should.ThrowAsync<InvalidOperationException>(() => Handler.HandleAsync(Command(Key), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ConflictThatTheNextReadSeesAsFree_ReservesOnTheSecondAttempt()
    {
        UnitOfWork.Scope.Accounts.TryReserveCreationKeyAsync(Arg.Any<AccountCreationKeyReservation>(), Arg.Any<CancellationToken>())
            .Returns(false, true);
        UnitOfWork.Scope.Accounts.FindCreationKeyAsync(Arg.Any<string>(), Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>())
            .Returns((AccountCreationKeyRecord?)null);

        var result = await Handler.HandleAsync(Command(Key), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReplay.ShouldBeFalse();
        await UnitOfWork.Scope.Accounts.Received(2)
            .TryReserveCreationKeyAsync(Arg.Any<AccountCreationKeyReservation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ConflictWithNoRecordToReadBack_Throws()
    {
        ConflictWith(null);

        await Should.ThrowAsync<InvalidOperationException>(() => Handler.HandleAsync(Command(Key), CancellationToken.None));
        Operation.Received(1).Outcome("failed");
    }

    [Fact]
    public async Task HandleAsync_ReservedKeyButAnAccountIdThatAlreadyExists_Throws()
    {
        UnitOfWork.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);

        await Should.ThrowAsync<InvalidOperationException>(() => Handler.HandleAsync(Command(Key), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_TheOwnCommitRepeatedByTheRetry_IsNotAReplayAndIsCountedOnce()
    {
        var twoRuns = new InlineUnitOfWork(2);
        twoRuns.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>()).Returns(CreatedAt);
        twoRuns.Scope.Accounts.TryReserveCreationKeyAsync(Arg.Any<AccountCreationKeyReservation>(), Arg.Any<CancellationToken>())
            .Returns(true, false);
        twoRuns.Scope.Accounts.FindCreationKeyAsync(Arg.Any<string>(), Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>())
            .Returns(new AccountCreationKeyRecord(
                AccountCreationRequestHash.Compute(Currency, Money.Create(0m, Currency).Value, Index),
                AccountCreationRequestHash.CurrentVersion,
                Generated,
                CreatedAt));
        var handler = new CreateAccountHandler(twoRuns, Protector, Ids, Telemetry, Logger);

        var result = await handler.HandleAsync(Command(Key), CancellationToken.None);

        result.Value.IsReplay.ShouldBeFalse();
        result.Value.AccountId.ShouldBe(Generated);
        Operation.Received(1).Outcome("already_created");
        Telemetry.Received(1).AccountCreated();
        await twoRuns.Scope.Audit.Received(1).RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_KeyProviderUnavailableWhileComparingTheReplay_PropagatesAsUnavailable()
    {
        ConflictWith(RecordFor(Index));
        Protector.BlindIndexCandidates(Arg.Any<HolderDocument>()).Throws(new KeyProviderUnavailableException());

        await Should.ThrowAsync<KeyProviderUnavailableException>(
            () => Handler.HandleAsync(Command(Key), CancellationToken.None));

        Operation.Received(1).Outcome("key_unavailable");
    }

    [Fact]
    public async Task HandleAsync_TokenReachesTheKeyStatements()
    {
        ConflictWith(RecordFor(Index));
        using var source = new CancellationTokenSource();

        await Handler.HandleAsync(Command(Key), source.Token);

        await UnitOfWork.Scope.Accounts.Received(1)
            .TryReserveCreationKeyAsync(Arg.Any<AccountCreationKeyReservation>(), source.Token);
        await UnitOfWork.Scope.Accounts.Received(1)
            .FindCreationKeyAsync(ClientId, Key, source.Token);
    }

    [Fact]
    public void ToString_OfTheReservationAndTheRecord_NeverPrintsTheKeyOrTheHash()
    {
        var reservation = new AccountCreationKeyReservation(ClientId, Key, new byte[] { 1, 2, 3 }, 1, Generated);
        var record = RecordFor(Index);

        reservation.ToString().ShouldNotContain(Key.Value);
        reservation.ToString().ShouldNotContain("ReadOnlyMemory");
        record.ToString().ShouldNotContain("ReadOnlyMemory");
        record.ToString().ShouldContain("HashVersion = 1");
    }
}
