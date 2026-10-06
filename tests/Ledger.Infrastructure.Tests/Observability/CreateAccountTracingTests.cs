using System.Diagnostics;
using Ledger.Application.Abstractions;
using Ledger.Application.Accounts;
using Ledger.Application.Security;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Application.Tests.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Tests.Observability.Support;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class CreateAccountTracingTests : IDisposable
{
    private static readonly Guid AccountGuid = Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 14, 12, 0, 3, TimeSpan.Zero);

    private readonly TestTelemetry _telemetry = new();
    private readonly IHolderDocumentProtector _protector = Substitute.For<IHolderDocumentProtector>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly CapturingLogger<CreateAccountHandler> _logger = new();

    public CreateAccountTracingTests()
    {
        _ids.NewId().Returns(AccountGuid);
        _protector.Protect(Arg.Any<HolderDocument>(), Arg.Any<AccountId>())
            .Returns(new ProtectedHolderDocument(new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6 }, 1));
    }

    public void Dispose()
    {
        _telemetry.Dispose();
    }

    private static CreateAccountCommand Command() =>
        new(
            HolderDocument.From("123.456.789-09").Value,
            "BRL",
            Money.Create(0m, "BRL").Value,
            "pix-core",
            "5d1b7c0e9a3f4c28b6e1d04f7a92c3b8");

    private CreateAccountHandler Handler(InlineUnitOfWork unitOfWork) =>
        new(unitOfWork, _protector, _ids, _telemetry.Security, _logger);

    private static InlineUnitOfWork Creating(int runs = 1)
    {
        var unitOfWork = new InlineUnitOfWork(runs);
        unitOfWork.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>()).Returns(CreatedAt);

        return unitOfWork;
    }

    private string Outcome() =>
        _telemetry.Capture.SingleActivity("ledger.create_account").GetTagItem("ledger.outcome").ShouldBeOfType<string>();

    [Fact]
    public async Task HandleAsync_NewAccount_ReportsCreatedAndCountsOneAccount()
    {
        await Handler(Creating()).HandleAsync(Command(), CancellationToken.None);

        Outcome().ShouldBe("created");
        _telemetry.Capture.Sum("ledger.accounts.created").ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_AccountAlreadyCreatedByAnEarlierAttempt_ReportsAlreadyCreated()
    {
        var twoRuns = new InlineUnitOfWork(2);
        twoRuns.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>())
            .Returns(CreatedAt, (DateTimeOffset?)null);
        twoRuns.Scope.Accounts.GetCreatedAtAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(CreatedAt);

        await Handler(twoRuns).HandleAsync(Command(), CancellationToken.None);

        Outcome().ShouldBe("already_created");
        _telemetry.Capture.Sum("ledger.accounts.created").ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_KeyProviderUnavailable_ReportsKeyUnavailableAndFlagsTheSpan()
    {
        _protector.Protect(Arg.Any<HolderDocument>(), Arg.Any<AccountId>()).Throws(new KeyProviderUnavailableException());

        await Should.ThrowAsync<KeyProviderUnavailableException>(
            () => Handler(Creating()).HandleAsync(Command(), CancellationToken.None));

        Outcome().ShouldBe("key_unavailable");
        _telemetry.Capture.SingleActivity("ledger.create_account").Status.ShouldBe(ActivityStatusCode.Error);
        _telemetry.Capture.Of("ledger.accounts.created").ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_UnexpectedFailure_ReportsFailed()
    {
        var failing = new InlineUnitOfWork();
        failing.Scope.Accounts.CreateAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(
            () => Handler(failing).HandleAsync(Command(), CancellationToken.None));

        Outcome().ShouldBe("failed");
        _telemetry.Capture.SingleActivity("ledger.create_account").Status.ShouldBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task HandleAsync_ValidationFailureBeforeTheOperation_OpensNoSpan()
    {
        var command = new CreateAccountCommand(
            HolderDocument.From("123.456.789-09").Value,
            "EUR",
            Money.Create(0m, "EUR").Value,
            "pix-core",
            "5d1b7c0e9a3f4c28b6e1d04f7a92c3b8");

        await Handler(Creating()).HandleAsync(command, CancellationToken.None);

        _telemetry.Capture.Activities.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_NeverPutsTheDocumentOrTheKeyMaterialInTheSpan()
    {
        await Handler(Creating()).HandleAsync(Command(), CancellationToken.None);

        var text = LeakScanner.TextOf(_telemetry.Capture);

        LeakScanner.Find(text, ["123.456.789-09", "12345678909", "5d1b7c0e9a3f4c28b6e1d04f7a92c3b8"]).ShouldBeEmpty();
    }
}
