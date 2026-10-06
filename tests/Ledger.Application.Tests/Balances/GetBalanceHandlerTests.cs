using Ledger.Application.Abstractions;
using Ledger.Application.Balances;
using Ledger.Application.Tests.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Application.Tests.Balances;

[Trait("Category", "Unit")]
public sealed class GetBalanceHandlerTests
{
    private const string ClientId = "pix-core";
    private const string CorrelationId = "5d1b7c0e9a3f4c28b6e1d04f7a92c3b8";

    private static readonly AccountId Account = AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;
    private static readonly EntryId LastEntry = EntryId.From(Guid.Parse("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10")).Value;
    private static readonly DateTimeOffset DatabaseNow = new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero).AddTicks(9_073_400);

    private readonly IBalanceReader _reader = Substitute.For<IBalanceReader>();
    private readonly IReadTelemetry _telemetry = Substitute.For<IReadTelemetry>();
    private IDisposable Operation { get; } = Substitute.For<IDisposable>();
    private CapturingLoggerFactory Loggers { get; } = new();

    public GetBalanceHandlerTests()
    {
        _telemetry.BeginBalance(Arg.Any<string>()).Returns(Operation);
    }

    private CapturingLogger Audit => Loggers.For(ReadAudit.CategoryName);

    private static GetBalanceQuery Query(DateTimeOffset? asOf = null) => new(Account, asOf, ClientId, CorrelationId);

    private static CurrentBalanceReading Current(
        decimal balance = 850.00m,
        decimal limit = 0m,
        EntryId? lastEntry = null,
        string currency = "BRL") =>
        new(currency, balance, limit, lastEntry ?? LastEntry, DatabaseNow);

    private static BalanceAtReading Historical(
        decimal? balanceAfter = 500.00m,
        decimal limit = 0m,
        EntryId? lastEntry = null) =>
        new("BRL", limit, balanceAfter, balanceAfter is null ? null : lastEntry ?? LastEntry, DatabaseNow);

    private GetBalanceHandler Handler(TimeSpan? window = null) =>
        new(_reader, new BalanceReadSettings(window ?? TimeSpan.FromSeconds(5)), _telemetry, new ReadAudit(Loggers));

    private void CurrentReturns(Result<CurrentBalanceReading> reading) =>
        _reader.ReadCurrentAsync(Account, Arg.Any<CancellationToken>()).Returns(reading);

    private void HistoricalReturns(Result<BalanceAtReading> reading) =>
        _reader.ReadAtAsync(Account, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(reading);

    [Fact]
    public async Task HandleAsync_CurrentBalance_BuildsTheViewWithMoneyInTheAccountCurrency()
    {
        CurrentReturns(Current(850.00m, 50.00m));

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        var view = result.Value;
        view.AccountId.ShouldBe(Account);
        view.Balance.ShouldBe(Money.Create(850.00m, "BRL").Value);
        view.OverdraftLimit.ShouldBe(Money.Create(50.00m, "BRL").Value);
        view.LastEntryId.ShouldBe(LastEntry);
        view.AsOf.ShouldBe(DatabaseNow);
        view.Settled.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_CurrentBalanceInsideTheOverdraft_KeepsTheNegativeBalance()
    {
        CurrentReturns(Current(-50.00m, 50.00m));

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        result.Value.Balance.ToDecimalString().ShouldBe("-50.00");
        result.Value.OverdraftLimit.ToDecimalString().ShouldBe("50.00");
    }

    [Fact]
    public async Task HandleAsync_AccountWithoutEntries_ReturnsZeroAndNoLastEntry()
    {
        CurrentReturns(new CurrentBalanceReading("BRL", 0m, 0m, null, DatabaseNow));

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        result.Value.Balance.ToDecimalString().ShouldBe("0.00");
        result.Value.LastEntryId.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_CurrentBalanceOfAnUnknownAccount_PassesNotFoundAhead()
    {
        CurrentReturns(AccountErrors.NotFound);

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.NotFound);
    }

    [Fact]
    public async Task HandleAsync_ReaderThrows_TheExceptionIsNotCaptured()
    {
        _reader.ReadCurrentAsync(Account, Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(() => Handler().HandleAsync(Query(), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_StoredCurrencyIsInvalid_ThrowsInvalidOperationException()
    {
        CurrentReturns(Current(currency: "br"));

        await Should.ThrowAsync<InvalidOperationException>(() => Handler().HandleAsync(Query(), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_TokenReachesTheReader()
    {
        CurrentReturns(Current());
        using var source = new CancellationTokenSource();

        await Handler().HandleAsync(Query(), source.Token);

        await _reader.Received(1).ReadCurrentAsync(Account, source.Token);
    }

    [Fact]
    public async Task HandleAsync_HistoricalBalance_ReturnsTheBalanceAfterOfTheLastEntryAtTheInstant()
    {
        var asOf = DatabaseNow.AddMinutes(-10);
        HistoricalReturns(Historical(500.00m, 20.00m));

        var result = await Handler().HandleAsync(Query(asOf), CancellationToken.None);

        var view = result.Value;
        view.Balance.ToDecimalString().ShouldBe("500.00");
        view.OverdraftLimit.ToDecimalString().ShouldBe("20.00");
        view.LastEntryId.ShouldBe(LastEntry);
        view.AsOf.ShouldBe(asOf);
    }

    [Fact]
    public async Task HandleAsync_HistoricalBalanceBeforeTheFirstEntry_ReturnsZeroAndNoLastEntry()
    {
        HistoricalReturns(Historical(null));

        var result = await Handler().HandleAsync(Query(DatabaseNow.AddDays(-1)), CancellationToken.None);

        result.Value.Balance.ToDecimalString().ShouldBe("0.00");
        result.Value.LastEntryId.ShouldBeNull();
        result.Value.Settled.ShouldBe(true);
    }

    [Fact]
    public async Task HandleAsync_HistoricalBalanceOfAnUnknownAccount_PassesNotFoundAheadEvenForTheFuture()
    {
        HistoricalReturns(AccountErrors.NotFound);

        var result = await Handler().HandleAsync(Query(DatabaseNow.AddHours(1)), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.NotFound);
    }

    [Fact]
    public async Task HandleAsync_AsOfOneMicrosecondAfterTheDatabaseClock_ReturnsAsOfInTheFuture()
    {
        HistoricalReturns(Historical());

        var result = await Handler().HandleAsync(Query(DatabaseNow.AddTicks(10)), CancellationToken.None);

        result.Error.ShouldBe(BalanceErrors.AsOfInTheFuture);
    }

    [Fact]
    public async Task HandleAsync_AsOfEqualToTheDatabaseClock_IsAccepted()
    {
        HistoricalReturns(Historical());

        var result = await Handler().HandleAsync(Query(DatabaseNow), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Settled.ShouldBe(false);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(60)]
    public async Task HandleAsync_AsOfExactlyOneWindowBack_IsSettled(int windowSeconds)
    {
        var window = TimeSpan.FromSeconds(windowSeconds);
        HistoricalReturns(Historical());

        var result = await Handler(window).HandleAsync(Query(DatabaseNow - window), CancellationToken.None);

        result.Value.Settled.ShouldBe(true);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(60)]
    public async Task HandleAsync_AsOfOneMicrosecondInsideTheWindow_IsNotSettled(int windowSeconds)
    {
        var window = TimeSpan.FromSeconds(windowSeconds);
        HistoricalReturns(Historical());

        var result = await Handler(window).HandleAsync(
            Query(DatabaseNow - window + TimeSpan.FromTicks(10)),
            CancellationToken.None);

        result.Value.Settled.ShouldBe(false);
    }

    [Fact]
    public async Task HandleAsync_CurrentBalance_NeverReportsSettled()
    {
        CurrentReturns(Current());

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        result.Value.Settled.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_CurrentBalance_OpensTheCurrentOperationAndDisposesIt()
    {
        CurrentReturns(Current());

        await Handler().HandleAsync(Query(), CancellationToken.None);

        _telemetry.Received(1).BeginBalance("current");
        Operation.Received(1).Dispose();
    }

    [Fact]
    public async Task HandleAsync_HistoricalBalance_OpensTheAsOfOperationAndDisposesIt()
    {
        HistoricalReturns(Historical());

        await Handler().HandleAsync(Query(DatabaseNow.AddHours(-1)), CancellationToken.None);

        _telemetry.Received(1).BeginBalance("as_of");
        Operation.Received(1).Dispose();
    }

    [Fact]
    public async Task HandleAsync_ReaderThrows_StillDisposesTheOperation()
    {
        _reader.ReadCurrentAsync(Account, Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(() => Handler().HandleAsync(Query(), CancellationToken.None));

        Operation.Received(1).Dispose();
    }

    [Fact]
    public async Task HandleAsync_CurrentBalance_WritesExactlyOneAuditEventWithTheRequestProperties()
    {
        CurrentReturns(Current());

        await Handler().HandleAsync(Query(), CancellationToken.None);

        var log = Audit.Entries.Single();
        log.EventId.Id.ShouldBe(2001);
        log.Level.ShouldBe(LogLevel.Information);
        log.Properties["ClientId"].ShouldBe(ClientId);
        log.Properties["AccountId"].ShouldBe(Account.ToString());
        log.Properties["CorrelationId"].ShouldBe(CorrelationId);
        log.Properties["Mode"].ShouldBe("CURRENT");
        log.Properties["AsOf"].ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task HandleAsync_HistoricalBalance_WritesTheAuditEventWithTheRequestedInstant()
    {
        var asOf = DatabaseNow.AddHours(-3);
        HistoricalReturns(Historical());

        await Handler().HandleAsync(Query(asOf), CancellationToken.None);

        var log = Audit.Single(2001);
        log.Properties["Mode"].ShouldBe("AS_OF");
        log.Properties["AsOf"].ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task HandleAsync_Success_NeverWritesTheBalanceInTheAuditEvent()
    {
        CurrentReturns(Current(850.00m));

        await Handler().HandleAsync(Query(), CancellationToken.None);

        var log = Audit.Entries.Single();
        log.Message.ShouldNotContain("850");
        log.Properties.Keys.ShouldNotContain("Balance");
    }

    [Fact]
    public async Task HandleAsync_NotFound_WritesNoAuditEvent()
    {
        CurrentReturns(AccountErrors.NotFound);

        await Handler().HandleAsync(Query(), CancellationToken.None);

        Loggers.Loggers.Values.SelectMany(logger => logger.Entries).ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_AsOfInTheFuture_WritesNoAuditEvent()
    {
        HistoricalReturns(Historical());

        await Handler().HandleAsync(Query(DatabaseNow.AddHours(1)), CancellationToken.None);

        Loggers.Loggers.Values.SelectMany(logger => logger.Entries).ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ReaderThrows_WritesNoAuditEvent()
    {
        _reader.ReadCurrentAsync(Account, Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(() => Handler().HandleAsync(Query(), CancellationToken.None));

        Loggers.Loggers.Values.SelectMany(logger => logger.Entries).ShouldBeEmpty();
    }

    [Fact]
    public void ToString_OfTheView_PrintsOnlyTheAccountId()
    {
        var view = new BalanceView(
            Account,
            Money.Create(850.00m, "BRL").Value,
            Money.Create(0m, "BRL").Value,
            LastEntry,
            DatabaseNow,
            null);

        var text = view.ToString();

        text.ShouldContain(Account.ToString());
        text.ShouldNotContain("850");
    }

    [Fact]
    public async Task HandleAsync_AsOfInTheBrasiliaOffset_ReadsViewsAndAuditsTheInstantInUtc()
    {
        var asOf = new DateTimeOffset(2026, 10, 1, 11, 3, 10, TimeSpan.FromHours(-3));
        HistoricalReturns(Historical());

        var result = await Handler().HandleAsync(Query(asOf), CancellationToken.None);

        await _reader.Received(1).ReadAtAsync(
            Account,
            Arg.Is<DateTimeOffset>(value => value.Offset == TimeSpan.Zero),
            Arg.Any<CancellationToken>());
        result.Value.AsOf.Offset.ShouldBe(TimeSpan.Zero);
        var log = Audit.Single(2001);
        log.Message.ShouldContain("2026-10-01T14:03:10.0000000+00:00");
        log.Message.ShouldNotContain("-03:00");
    }
}
