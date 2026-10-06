using System.Globalization;
using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Audit;
using Ledger.Application.Security;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Application.Tests.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Application.Tests.Accounts;

[Trait("Category", "Unit")]
public sealed class CreateAccountHandlerTests
{
    private const string ClientId = "pix-core";
    private const string CorrelationId = "5d1b7c0e9a3f4c28b6e1d04f7a92c3b8";
    private const string RawDocument = "123.456.789-09";
    private const string NormalizedDocument = "12345678909";

    private static readonly Guid AccountGuid = Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");
    private static readonly DateTimeOffset CreatedAt = new DateTimeOffset(2026, 9, 14, 12, 0, 3, TimeSpan.Zero).AddTicks(4_152_060);
    private static readonly byte[] Blob = [1, 0, 1, 9, 9, 9, 9, 9];
    private static readonly byte[] Index = [7, 7, 7, 7];

    private InlineUnitOfWork UnitOfWork { get; }

    private IHolderDocumentProtector Protector { get; } = Substitute.For<IHolderDocumentProtector>();

    private IIdGenerator Ids { get; } = Substitute.For<IIdGenerator>();

    private ISecurityTelemetry Telemetry { get; } = Substitute.For<ISecurityTelemetry>();

    private IAccountCreationOperation Operation { get; } = Substitute.For<IAccountCreationOperation>();

    private CapturingLogger<CreateAccountHandler> Logger { get; } = new();

    private CreateAccountHandler Handler { get; }

    public CreateAccountHandlerTests()
    {
        UnitOfWork = new InlineUnitOfWork();
        Ids.NewId().Returns(AccountGuid);
        Telemetry.BeginCreateAccount().Returns(Operation);
        Protector.Protect(Arg.Any<HolderDocument>(), Arg.Any<AccountId>()).Returns(new ProtectedHolderDocument(Blob, Index, 1));
        UnitOfWork.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>()).Returns(CreatedAt);
        Handler = new CreateAccountHandler(UnitOfWork, Protector, Ids, Telemetry, Logger);
    }

    private static CreateAccountCommand Command(
        string currency = "BRL",
        decimal overdraft = 0m,
        string overdraftCurrency = "BRL") =>
        new(
            HolderDocument.From(RawDocument).Value,
            currency,
            Money.Create(overdraft, overdraftCurrency).Value,
            ClientId,
            CorrelationId);

    private static AccountId ExpectedAccount => AccountId.From(AccountGuid).Value;

    [Fact]
    public async Task HandleAsync_ValidCommand_ReturnsTheCreatedAccountWithTheMaskAndTheDatabaseInstant()
    {
        var result = await Handler.HandleAsync(Command(overdraft: 50m), CancellationToken.None);

        var created = result.Value;
        created.AccountId.ShouldBe(ExpectedAccount);
        created.Currency.ShouldBe("BRL");
        created.OverdraftLimit.ToDecimalString().ShouldBe("50.00");
        created.HolderDocumentMasked.ShouldBe("***.***.789-**");
        created.CreatedAt.ShouldBe(CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_ProtectsTheDocumentWithTheGeneratedIdBeforeOpeningTheTransaction()
    {
        var protectedBeforeTransaction = -1;
        UnitOfWork.BeforeExecute = () => protectedBeforeTransaction = Protector.ReceivedCalls().Count();

        await Handler.HandleAsync(Command(), CancellationToken.None);

        protectedBeforeTransaction.ShouldBe(1);
        Protector.Received(1).Protect(Arg.Is<HolderDocument>(document => document.Normalized == NormalizedDocument), ExpectedAccount);
        Ids.Received(1).NewId();
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_InsertsTheAccountWithTheProtectedDocument()
    {
        await Handler.HandleAsync(Command(overdraft: 50m), CancellationToken.None);

        var account = UnitOfWork.Scope.Accounts.ReceivedCalls().Single().GetArguments().OfType<NewAccount>().Single();
        account.Id.ShouldBe(ExpectedAccount);
        account.Currency.ShouldBe("BRL");
        account.OverdraftLimit.ToDecimalString().ShouldBe("50.00");
        account.Document.Encrypted.ToArray().ShouldBe(Blob);
        account.Document.BlindIndex.ToArray().ShouldBe(Index);
        account.Document.KeyVersion.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_RecordsTheAccountCreatedEventInsideTheTransaction()
    {
        await Handler.HandleAsync(Command(), CancellationToken.None);

        var auditEvent = UnitOfWork.Scope.Audit.ReceivedCalls().Single().GetArguments().OfType<AuditEvent>().Single();
        auditEvent.EventType.ShouldBe("account.created");
        auditEvent.ClientId.ShouldBe(ClientId);
        auditEvent.AccountId.ShouldBe(ExpectedAccount);
        auditEvent.CorrelationId.ShouldBe(CorrelationId);
        auditEvent.DetailsJson.ShouldBe("{}");
        UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_FunctionRunsThreeTimes_UsesTheSameIdAndTheSameBlobInEveryExecution()
    {
        var threeRuns = new InlineUnitOfWork(3);
        threeRuns.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>()).Returns(CreatedAt);
        var handler = new CreateAccountHandler(threeRuns, Protector, Ids, Telemetry, Logger);

        await handler.HandleAsync(Command(), CancellationToken.None);

        var accounts = threeRuns.Scope.Accounts.ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .OfType<NewAccount>()
            .ToList();
        accounts.Count.ShouldBe(3);
        accounts.Select(account => account.Id).Distinct().Count().ShouldBe(1);
        accounts.Select(account => account.Document).Distinct().Count().ShouldBe(1);
        Ids.Received(1).NewId();
        Protector.Received(1).Protect(Arg.Any<HolderDocument>(), Arg.Any<AccountId>());
    }

    [Fact]
    public async Task HandleAsync_DuplicateIdOnTheFirstAttempt_ThrowsBecauseARepeatedIdIsADefect()
    {
        UnitOfWork.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);

        await Should.ThrowAsync<InvalidOperationException>(() => Handler.HandleAsync(Command(), CancellationToken.None));

        await UnitOfWork.Scope.Accounts.DidNotReceive().GetCreatedAtAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>());
        Operation.Received(1).Outcome("failed");
        Telemetry.DidNotReceive().AccountCreated();
    }

    [Fact]
    public async Task HandleAsync_DuplicateIdOnALaterAttempt_ReturnsTheSameAccountWithoutAuditingAgain()
    {
        var twoRuns = new InlineUnitOfWork(2);
        twoRuns.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>())
            .Returns(CreatedAt, (DateTimeOffset?)null);
        twoRuns.Scope.Accounts.GetCreatedAtAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(CreatedAt);
        var handler = new CreateAccountHandler(twoRuns, Protector, Ids, Telemetry, Logger);

        var result = await handler.HandleAsync(Command(), CancellationToken.None);

        result.Value.CreatedAt.ShouldBe(CreatedAt);
        result.Value.AccountId.ShouldBe(ExpectedAccount);
        await twoRuns.Scope.Audit.Received(1).RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
        await twoRuns.Scope.Accounts.Received(1).GetCreatedAtAsync(ExpectedAccount, Arg.Any<CancellationToken>());
        Operation.Received(1).Outcome("already_created");
        Telemetry.Received(1).AccountCreated();
    }

    [Fact]
    public async Task HandleAsync_DuplicateIdOnALaterAttemptButNoRowToReadBack_Throws()
    {
        var twoRuns = new InlineUnitOfWork(2);
        twoRuns.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>())
            .Returns(CreatedAt, (DateTimeOffset?)null);
        twoRuns.Scope.Accounts.GetCreatedAtAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);
        var handler = new CreateAccountHandler(twoRuns, Protector, Ids, Telemetry, Logger);

        await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(Command(), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_KeyProviderUnavailable_PropagatesWithoutOpeningTheTransaction()
    {
        Protector.Protect(Arg.Any<HolderDocument>(), Arg.Any<AccountId>())
            .Throws(new KeyProviderUnavailableException());

        await Should.ThrowAsync<KeyProviderUnavailableException>(
            () => Handler.HandleAsync(Command(), CancellationToken.None));

        UnitOfWork.Calls.ShouldBe(0);
        Operation.Received(1).Outcome("key_unavailable");
        Operation.Received(1).Dispose();
        Telemetry.DidNotReceive().AccountCreated();
        var log = Logger.Single(7002);
        log.Level.ShouldBe(LogLevel.Warning);
        log.Properties["Operation"].ShouldBe("create_account");
    }

    [Fact]
    public async Task HandleAsync_CreatedAccount_CountsOnceLogsAndClosesTheOperation()
    {
        await Handler.HandleAsync(Command(), CancellationToken.None);

        Telemetry.Received(1).AccountCreated();
        Telemetry.Received(1).BeginCreateAccount();
        Operation.Received(1).Outcome("created");
        Operation.Received(1).Dispose();
        var log = Logger.Single(7001);
        log.Level.ShouldBe(LogLevel.Information);
        log.Properties["AccountId"].ShouldBe(ExpectedAccount.ToString());
        log.Properties["ClientId"].ShouldBe(ClientId);
    }

    [Fact]
    public async Task HandleAsync_FunctionRunsTwice_CountsTheAccountOnlyOnce()
    {
        var twoRuns = new InlineUnitOfWork(2);
        twoRuns.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>()).Returns(CreatedAt);
        var handler = new CreateAccountHandler(twoRuns, Protector, Ids, Telemetry, Logger);

        await handler.HandleAsync(Command(), CancellationToken.None);

        Telemetry.Received(1).AccountCreated();
        Logger.Entries.Count(entry => entry.EventId.Id == 7001).ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_UnexpectedException_MarksTheOutcomeAsFailedAndPropagates()
    {
        UnitOfWork.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(() => Handler.HandleAsync(Command(), CancellationToken.None));

        Operation.Received(1).Outcome("failed");
        Operation.Received(1).Dispose();
        UnitOfWork.Rollbacks.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_UnsupportedCurrency_ReturnsTheDomainErrorWithoutSideEffects()
    {
        var result = await Handler.HandleAsync(Command("EUR", overdraftCurrency: "EUR"), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.UnsupportedCurrency);
        UnitOfWork.Calls.ShouldBe(0);
        Protector.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_OverdraftInAnotherCurrency_ReturnsCurrencyMismatch()
    {
        var result = await Handler.HandleAsync(Command(overdraftCurrency: "EUR"), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.CurrencyMismatch);
        UnitOfWork.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-50.00")]
    [InlineData("1000000000.00")]
    [InlineData("9999999999999999.99")]
    public async Task HandleAsync_OverdraftOutsideTheRange_ReturnsInvalidOverdraftLimitWithoutSideEffects(string overdraft)
    {
        var command = Command(overdraft: decimal.Parse(overdraft, CultureInfo.InvariantCulture));

        var result = await Handler.HandleAsync(command, CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.InvalidOverdraftLimit);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        UnitOfWork.Calls.ShouldBe(0);
        Protector.ReceivedCalls().ShouldBeEmpty();
        Ids.ReceivedCalls().ShouldBeEmpty();
        Telemetry.DidNotReceive().BeginCreateAccount();
        await UnitOfWork.Scope.Accounts.DidNotReceive().CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>());
        await UnitOfWork.Scope.Audit.DidNotReceive().RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("0.00")]
    [InlineData("0.01")]
    [InlineData("999999999.99")]
    public async Task HandleAsync_OverdraftInsideTheRange_CreatesTheAccount(string overdraft)
    {
        var command = Command(overdraft: decimal.Parse(overdraft, CultureInfo.InvariantCulture));

        var result = await Handler.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OverdraftLimit.ToDecimalString().ShouldBe(overdraft);
        UnitOfWork.Calls.ShouldBe(1);
        Protector.Received(1).Protect(Arg.Any<HolderDocument>(), ExpectedAccount);
    }

    [Fact]
    public async Task HandleAsync_CanceledToken_ThrowsBeforeDoingAnything()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Handler.HandleAsync(Command(), source.Token));

        UnitOfWork.Calls.ShouldBe(0);
        Telemetry.DidNotReceive().BeginCreateAccount();
    }

    [Fact]
    public async Task HandleAsync_TokenReachesTheRepositoryAndTheAuditTrail()
    {
        using var source = new CancellationTokenSource();

        await Handler.HandleAsync(Command(), source.Token);

        await UnitOfWork.Scope.Accounts.Received(1).CreateAsync(Arg.Any<NewAccount>(), source.Token);
        await UnitOfWork.Scope.Audit.Received(1).RecordAsync(Arg.Any<AuditEvent>(), source.Token);
    }

    [Fact]
    public async Task HandleAsync_ClearDocument_ReachesNothingButTheProtector()
    {
        await Handler.HandleAsync(Command(), CancellationToken.None);
        UnitOfWork.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);
        await Should.ThrowAsync<InvalidOperationException>(() => Handler.HandleAsync(Command(), CancellationToken.None));

        var texts = UnitOfWork.Scope.Accounts.ReceivedCalls()
            .Concat(UnitOfWork.Scope.Audit.ReceivedCalls())
            .Concat(Telemetry.ReceivedCalls())
            .Concat(Operation.ReceivedCalls())
            .SelectMany(call => call.GetArguments())
            .Select(argument => argument?.ToString() ?? string.Empty)
            .Concat(Logger.Entries.Select(entry => entry.Message))
            .Concat(Logger.Entries.SelectMany(entry => entry.Properties.Values.Select(value => value ?? string.Empty)))
            .ToList();

        texts.ShouldAllBe(text => !text.Contains(NormalizedDocument) && !text.Contains(RawDocument));
    }

    [Fact]
    public void ToString_OfTheCommand_NeverPrintsTheDocument()
    {
        var text = Command().ToString();

        text.ShouldNotContain(NormalizedDocument);
        text.ShouldNotContain("789");
    }

    [Fact]
    public void ToString_OfTheProtectedDocumentAndTheNewAccount_NeverPrintsBytes()
    {
        var document = new ProtectedHolderDocument(Blob, Index, 1);
        var account = new NewAccount(ExpectedAccount, "BRL", Money.Create(0m, "BRL").Value, document);

        document.ToString().ShouldNotContain("9");
        account.ToString().ShouldNotContain("ReadOnlyMemory");
    }
}
