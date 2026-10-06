using Ledger.Application.Abstractions;
using Ledger.Application.Entries;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Application.Tests.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Application.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class ListEntriesHandlerTests
{
    private const string ProtectedCursor = "AQAGXMfgSmQhAAAAAAAABzNHjoppo-BF0VHKGd-gMKI7";

    private static readonly DateTimeOffset Base = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly IStatementReader _reader = Substitute.For<IStatementReader>();
    private readonly IStatementCursorProtector _protector = Substitute.For<IStatementCursorProtector>();
    private readonly IReadTelemetry _telemetry = Substitute.For<IReadTelemetry>();
    private IStatementOperation Operation { get; } = Substitute.For<IStatementOperation>();
    private CapturingLoggerFactory Loggers { get; } = new();

    public ListEntriesHandlerTests()
    {
        _telemetry.BeginStatement(Arg.Any<int>()).Returns(Operation);
        _protector.Protect(Arg.Any<AccountId>(), Arg.Any<StatementPosition>()).Returns(ProtectedCursor);
        _reader.AccountExistsAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private ListEntriesHandler Handler => new(_reader, _protector, _telemetry, new ReadAudit(Loggers));

    private CapturingLogger Audit => Loggers.For(ReadAudit.CategoryName);

    private static ListEntriesQuery Query(
        int limit = 3,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        StatementPosition? cursor = null) =>
        new(EntryFixtures.Account, from, to, limit, cursor, EntryFixtures.ClientId, EntryFixtures.CorrelationId);

    private static List<EntryView> Rows(int count)
    {
        var rows = new List<EntryView>();

        for (var index = 0; index < count; index++)
        {
            var position = count - index;
            var recordedAt = Base.AddMinutes(position);
            var id = EntryId.From(new Guid(position, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1])).Value;

            rows.Add(EntryFixtures.StoredView() with { Id = id, AccountVersion = position, RecordedAt = recordedAt });
        }

        return rows;
    }

    private void ReaderReturns(IReadOnlyList<EntryView> rows) =>
        _reader.ReadPageAsync(
                Arg.Any<AccountId>(),
                Arg.Any<StatementBounds>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(rows);

    [Fact]
    public async Task HandleAsync_Query_AsksTheReaderForOneMoreRowThanTheLimit()
    {
        ReaderReturns(Rows(3));

        await Handler.HandleAsync(Query(limit: 3), CancellationToken.None);

        await _reader.Received(1).ReadPageAsync(
            EntryFixtures.Account,
            Arg.Any<StatementBounds>(),
            4,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_QueryWithPeriodAndCursor_PassesTheResolvedBounds()
    {
        ReaderReturns(Rows(1));
        var from = Base.AddHours(-2);
        var to = Base.AddHours(1);
        var cursor = new StatementPosition(Base, 40);

        await Handler.HandleAsync(Query(from: from, to: to, cursor: cursor), CancellationToken.None);

        await _reader.Received(1).ReadPageAsync(
            EntryFixtures.Account,
            StatementBounds.Resolve(from, to, cursor),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ReaderReturnsLimitPlusOne_KeepsLimitItemsAndBuildsTheCursorFromTheLastKept()
    {
        var rows = Rows(4);
        ReaderReturns(rows);

        var result = await Handler.HandleAsync(Query(limit: 3), CancellationToken.None);

        var page = result.Value;
        page.Items.Count.ShouldBe(3);
        page.Items.ShouldBe(rows.Take(3));
        page.NextCursor.ShouldBe(ProtectedCursor);
        page.Limit.ShouldBe(3);
        _protector.Received(1).Protect(
            EntryFixtures.Account,
            new StatementPosition(rows[2].RecordedAt, rows[2].AccountVersion));
    }

    [Fact]
    public async Task HandleAsync_ReaderReturnsExactlyTheLimit_HasNoNextCursor()
    {
        ReaderReturns(Rows(3));

        var result = await Handler.HandleAsync(Query(limit: 3), CancellationToken.None);

        result.Value.Items.Count.ShouldBe(3);
        result.Value.NextCursor.ShouldBeNull();
        _protector.DidNotReceive().Protect(Arg.Any<AccountId>(), Arg.Any<StatementPosition>());
    }

    [Fact]
    public async Task HandleAsync_ReaderReturnsFewerThanTheLimit_HasNoNextCursor()
    {
        ReaderReturns(Rows(2));

        var result = await Handler.HandleAsync(Query(limit: 3), CancellationToken.None);

        result.Value.Items.Count.ShouldBe(2);
        result.Value.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_EmptyFirstPageOfAnExistingAccount_ReturnsAnEmptyPage()
    {
        ReaderReturns([]);

        var result = await Handler.HandleAsync(Query(), CancellationToken.None);

        result.Value.Items.ShouldBeEmpty();
        result.Value.NextCursor.ShouldBeNull();
        await _reader.Received(1).AccountExistsAsync(EntryFixtures.Account, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EmptyFirstPageOfAnUnknownAccount_ReturnsAccountNotFound()
    {
        ReaderReturns([]);
        _reader.AccountExistsAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Handler.HandleAsync(Query(), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.NotFound);
    }

    [Fact]
    public async Task HandleAsync_EmptyPageWithCursor_DoesNotCheckTheAccount()
    {
        ReaderReturns([]);

        var result = await Handler.HandleAsync(Query(cursor: new StatementPosition(Base, 1)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _reader.DidNotReceive().AccountExistsAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NonEmptyPage_DoesNotCheckTheAccount()
    {
        ReaderReturns(Rows(1));

        await Handler.HandleAsync(Query(), CancellationToken.None);

        await _reader.DidNotReceive().AccountExistsAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ReaderThrows_TheExceptionPropagatesAndTheOperationIsDisposed()
    {
        _reader.ReadPageAsync(
                Arg.Any<AccountId>(),
                Arg.Any<StatementBounds>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(() => Handler.HandleAsync(Query(), CancellationToken.None));

        Operation.Received(1).Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HandleAsync_LimitBelowOne_Throws(int limit)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => Handler.HandleAsync(Query(limit: limit), CancellationToken.None));

        await _reader.DidNotReceive().ReadPageAsync(
            Arg.Any<AccountId>(),
            Arg.Any<StatementBounds>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TokenReachesTheReader()
    {
        ReaderReturns(Rows(1));
        using var source = new CancellationTokenSource();

        await Handler.HandleAsync(Query(), source.Token);

        await _reader.Received(1).ReadPageAsync(
            Arg.Any<AccountId>(),
            Arg.Any<StatementBounds>(),
            Arg.Any<int>(),
            source.Token);
    }

    [Fact]
    public async Task HandleAsync_Success_ReportsTheReturnedCountAndWhetherThereIsANextPage()
    {
        ReaderReturns(Rows(4));

        await Handler.HandleAsync(Query(limit: 3), CancellationToken.None);

        _telemetry.Received(1).BeginStatement(3);
        Operation.Received(1).Returned(3, true);
        Operation.Received(1).Dispose();
    }

    [Fact]
    public async Task HandleAsync_LastPage_ReportsNoNextPage()
    {
        ReaderReturns(Rows(2));

        await Handler.HandleAsync(Query(limit: 3), CancellationToken.None);

        Operation.Received(1).Returned(2, false);
    }

    [Fact]
    public async Task HandleAsync_Success_WritesOneAuditEventWithTheRequestShape()
    {
        ReaderReturns(Rows(2));
        var from = Base.AddHours(-2);
        var to = Base.AddHours(1);

        await Handler.HandleAsync(Query(limit: 3, from: from, to: to, cursor: new StatementPosition(Base, 9)), CancellationToken.None);

        var log = Audit.Entries.Single();
        log.EventId.Id.ShouldBe(2002);
        log.Level.ShouldBe(LogLevel.Information);
        log.Properties["ClientId"].ShouldBe(EntryFixtures.ClientId);
        log.Properties["AccountId"].ShouldBe(EntryFixtures.Account.ToString());
        log.Properties["CorrelationId"].ShouldBe(EntryFixtures.CorrelationId);
        log.Properties["Limit"].ShouldBe("3");
        log.Properties["Returned"].ShouldBe("2");
        log.Properties["HasCursor"].ShouldBe("True");
        log.Properties["From"].ShouldNotBeNullOrEmpty();
        log.Properties["To"].ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task HandleAsync_RequestWithoutCursor_AuditsHasCursorFalseAndNeverTheCursorText()
    {
        ReaderReturns(Rows(4));

        await Handler.HandleAsync(Query(limit: 3), CancellationToken.None);

        var log = Audit.Entries.Single();
        log.Properties["HasCursor"].ShouldBe("False");
        log.Message.ShouldNotContain(ProtectedCursor);
        log.Properties.Values.ShouldNotContain(ProtectedCursor);
    }

    [Fact]
    public async Task HandleAsync_AccountNotFound_WritesNoAuditEvent()
    {
        ReaderReturns([]);
        _reader.AccountExistsAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(false);

        await Handler.HandleAsync(Query(), CancellationToken.None);

        Audit.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ReaderThrows_WritesNoAuditEvent()
    {
        _reader.ReadPageAsync(
                Arg.Any<AccountId>(),
                Arg.Any<StatementBounds>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("db"));

        await Should.ThrowAsync<TimeoutException>(() => Handler.HandleAsync(Query(), CancellationToken.None));

        Audit.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void ToString_OfThePage_NeverPrintsTheCursorOrTheItems()
    {
        var page = new StatementPage(Rows(2), ProtectedCursor, 3);

        var text = page.ToString();

        text.ShouldNotContain(ProtectedCursor);
        text.ShouldContain("Returned = 2");
    }

    [Fact]
    public async Task HandleAsync_PeriodInTheBrasiliaOffset_AuditsTheInstantsInUtc()
    {
        ReaderReturns(Rows(1));
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-3));
        var to = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(-3));

        await Handler.HandleAsync(Query(from: from, to: to), CancellationToken.None);

        var log = Audit.Single(2002);
        log.Message.ShouldContain("from 2026-10-01T03:00:00.0000000+00:00, to 2026-10-02T03:00:00.0000000+00:00");
        log.Message.ShouldNotContain("-03:00");
    }

    [Fact]
    public async Task HandleAsync_PeriodInTheBrasiliaOffset_AsksTheReaderForBoundsInUtc()
    {
        ReaderReturns(Rows(1));
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-3));
        var to = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(-3));

        await Handler.HandleAsync(Query(from: from, to: to), CancellationToken.None);

        await _reader.Received(1).ReadPageAsync(
            EntryFixtures.Account,
            Arg.Is<StatementBounds>(bounds => bounds.From.HasValue
                                              && bounds.From.Value.Offset == TimeSpan.Zero
                                              && bounds.Upper.HasValue
                                              && bounds.Upper.Value.RecordedAt.Offset == TimeSpan.Zero),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }
}
