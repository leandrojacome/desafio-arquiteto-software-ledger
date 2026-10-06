using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Reads;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.TimeZones;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class StatementTimeZoneTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset At2330OfThe9th = new(2026, 3, 10, 2, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MidnightOfThe10th = new(2026, 3, 10, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset At0030OfThe10th = new(2026, 3, 10, 3, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset At2330OfThe10th = new(2026, 3, 11, 2, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastMicrosecondOfThe10th = new DateTimeOffset(2026, 3, 11, 3, 0, 0, TimeSpan.Zero).AddTicks(-10);
    private static readonly DateTimeOffset MidnightOfThe11th = new(2026, 3, 11, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset At0030OfThe11th = new(2026, 3, 11, 3, 30, 0, TimeSpan.Zero);

    private ReadWorld _world = null!;

    public Task InitializeAsync()
    {
        _world = ReadWorld.Create(postgres);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [DockerFact]
    public async Task Statement_ForTheBrasiliaDay_ReturnsOnlyTheEntriesRecordedBetweenItsTwoMidnights()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var response = await StatementAsync(accountId, "from=2026-03-10T00:00:00-03:00&to=2026-03-11T00:00:00-03:00");

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        Versions(response).ShouldBe([5, 4, 3, 2]);
    }

    [DockerFact]
    public async Task Statement_ForTheDayBeforeAndTheDayAfterInBrasilia_ReturnsTheirOwnEntries()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var before = await StatementAsync(accountId, "from=2026-03-09T00:00:00-03:00&to=2026-03-10T00:00:00-03:00");
        using var after = await StatementAsync(accountId, "from=2026-03-11T00:00:00-03:00&to=2026-03-12T00:00:00-03:00");

        Versions(before).ShouldBe([1]);
        Versions(after).ShouldBe([7, 6]);
    }

    [DockerFact]
    public async Task Statement_EntriesAt2330AndAt0030OfBrasilia_FallInDifferentDays()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var theNinth = await StatementAsync(accountId, "from=2026-03-09T00:00:00-03:00&to=2026-03-10T00:00:00-03:00");
        using var theTenth = await StatementAsync(accountId, "from=2026-03-10T00:00:00-03:00&to=2026-03-11T00:00:00-03:00");
        using var theEleventh = await StatementAsync(accountId, "from=2026-03-11T00:00:00-03:00&to=2026-03-12T00:00:00-03:00");

        Recorded(theNinth).ShouldBe([At2330OfThe9th]);
        Recorded(theTenth).ShouldContain(At0030OfThe10th);
        Recorded(theTenth).ShouldContain(At2330OfThe10th);
        Recorded(theTenth).ShouldNotContain(At2330OfThe9th);
        Recorded(theTenth).ShouldNotContain(At0030OfThe11th);
        Recorded(theEleventh).ShouldContain(At0030OfThe11th);
        Recorded(theEleventh).ShouldNotContain(At2330OfThe10th);
    }

    [DockerFact]
    public async Task Statement_AtTheMidnightsOfBrasilia_IncludesTheLowerBoundAndExcludesTheUpperBound()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var response = await StatementAsync(accountId, "from=2026-03-10T00:00:00-03:00&to=2026-03-11T00:00:00-03:00");

        Recorded(response).ShouldContain(MidnightOfThe10th);
        Recorded(response).ShouldContain(LastMicrosecondOfThe10th);
        Recorded(response).ShouldNotContain(MidnightOfThe11th);
    }

    [DockerFact]
    public async Task Statement_ForTheUtcDay_DiffersFromTheBrasiliaDayOfTheSameDate()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var utcDay = await StatementAsync(accountId, "from=2026-03-10T00:00:00Z&to=2026-03-11T00:00:00Z");
        using var brasiliaDay = await StatementAsync(accountId, "from=2026-03-10T00:00:00-03:00&to=2026-03-11T00:00:00-03:00");

        Versions(utcDay).ShouldBe([3, 2, 1]);
        Versions(brasiliaDay).ShouldBe([5, 4, 3, 2]);
    }

    [DockerFact]
    public async Task Statement_WithTheSameWindowWrittenInDifferentOffsets_ReturnsTheSameItemsInTheSameOrder()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();
        string[] windows =
        [
            "from=2026-03-10T03:00:00Z&to=2026-03-11T03:00:00Z",
            "from=2026-03-10T03:00:00%2B00:00&to=2026-03-11T03:00:00%2B00:00",
            "from=2026-03-10T00:00:00-03:00&to=2026-03-11T00:00:00-03:00",
            "from=2026-03-10T08:30:00%2B05:30&to=2026-03-11T08:30:00%2B05:30",
            "from=2026-03-10T17:00:00%2B14:00&to=2026-03-11T17:00:00%2B14:00",
            "from=2026-03-09T13:00:00-14:00&to=2026-03-10T13:00:00-14:00",
            "from=2026-03-10T03:00:00Z&to=2026-03-11T00:00:00-03:00"
        ];

        var results = new List<string>();

        foreach (var window in windows)
        {
            using var response = await StatementAsync(accountId, window);

            response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
            results.Add(string.Join(',', StatementItem.ItemsOf(response).Select(item => item.EntryId)));
        }

        results.Distinct().Count().ShouldBe(1);
        results[0].Split(',').Length.ShouldBe(4);
    }

    [DockerFact]
    public async Task Statement_WithBoundsInBrasilia_AnswersEveryInstantInUtcWithZ()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var response = await StatementAsync(accountId, "from=2026-03-10T00:00:00-03:00&to=2026-03-11T00:00:00-03:00");

        var items = StatementItem.ItemsOf(response);

        items.ShouldNotBeEmpty();
        items.ShouldAllBe(item => item.RecordedAt.EndsWith('Z') && item.OccurredAt.EndsWith('Z'));
        items.ShouldAllBe(item => !item.RecordedAt.Contains("-03:00") && !item.OccurredAt.Contains("-03:00"));
        response.Body.ShouldNotContain("-03:00");
    }

    [DockerFact]
    public async Task Statement_WalkedPageByPageWithBoundsInBrasilia_ReturnsTheItemsOfTheEquivalentUtcBounds()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        var inBrasilia = await _world.Client.WalkAsync(accountId, 2, "2026-03-10T00:00:00-03:00", "2026-03-11T00:00:00-03:00");
        var inUtc = await _world.Client.WalkAsync(accountId, 2, "2026-03-10T03:00:00Z", "2026-03-11T03:00:00Z");

        inBrasilia.Items.Select(item => item.EntryId).ShouldBe(inUtc.Items.Select(item => item.EntryId));
        inBrasilia.Items.Select(item => item.AccountVersion).ShouldBe([5L, 4L, 3L, 2L]);
        inBrasilia.PageSizes.ShouldBe(inUtc.PageSizes);
        inBrasilia.PageSizes.ShouldBe([2, 2]);
    }

    [DockerFact]
    public async Task Statement_CursorTakenWithBoundsInBrasilia_ContinuesWithBoundsInUtcAndInAnotherOffset()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var first = await StatementAsync(accountId, "limit=2&from=2026-03-10T00:00:00-03:00&to=2026-03-11T00:00:00-03:00");
        var cursor = Uri.EscapeDataString(first.Text("nextCursor"));

        using var inUtc = await StatementAsync(
            accountId,
            $"limit=2&from=2026-03-10T03:00:00Z&to=2026-03-11T03:00:00Z&cursor={cursor}");
        using var inIndia = await StatementAsync(
            accountId,
            $"limit=2&from=2026-03-10T08:30:00%2B05:30&to=2026-03-11T08:30:00%2B05:30&cursor={cursor}");
        using var withoutBounds = await StatementAsync(accountId, $"limit=2&cursor={cursor}");

        Versions(first).ShouldBe([5, 4]);
        Versions(inUtc).ShouldBe([3, 2]);
        Versions(inIndia).ShouldBe([3, 2]);
        Versions(withoutBounds).ShouldBe([3, 2]);
    }

    [DockerFact]
    public async Task Statement_Order_IsNewestFirstWhateverTheOffsetOfTheBounds()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        foreach (var window in new[]
                 {
                     "from=2026-03-09T00:00:00-03:00&to=2026-03-12T00:00:00-03:00",
                     "from=2026-03-09T03:00:00Z&to=2026-03-12T03:00:00Z",
                     "from=2026-03-09T08:30:00%2B05:30&to=2026-03-12T08:30:00%2B05:30"
                 })
        {
            using var response = await StatementAsync(accountId, window);

            var recorded = Recorded(response);

            recorded.Count.ShouldBe(7);
            recorded.ShouldBe([.. recorded.OrderByDescending(instant => instant)]);
            Versions(response).ShouldBe([7, 6, 5, 4, 3, 2, 1]);
        }
    }

    [DockerFact]
    public async Task Statement_WithBoundsWithoutTimeZone_Returns400WithMissingTimeZoneOnEachField()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var response = await StatementAsync(accountId, "from=2026-03-10T00:00:00&to=2026-03-11T00:00:00");

        response.ShouldBeProblemCode(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        response.Errors().ShouldBe(
        [
            ("from", "MISSING_TIME_ZONE", "Informe o fuso horário no campo 'from', por exemplo 'Z' ou '-03:00'."),
            ("to", "MISSING_TIME_ZONE", "Informe o fuso horário no campo 'to', por exemplo 'Z' ou '-03:00'.")
        ]);
        response.ShouldCarryNoInternals();
        response.Body.ShouldNotContain("2026-03-10T00:00:00");
    }

    [DockerFact]
    public async Task Statement_WithAnInvalidOffset_Returns400InvalidFormatOnThatField()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var response = await StatementAsync(accountId, "from=2026-03-10T00:00:00%2B25:00&to=2026-03-11T00:00:00-15:00");

        response.ShouldBeProblemCode(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        response.Errors().ShouldBe(
        [
            (
                "from",
                "INVALID_FORMAT",
                "O campo 'from' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo."),
            (
                "to",
                "INVALID_FORMAT",
                "O campo 'to' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo.")
        ]);
    }

    [DockerFact]
    public async Task Statement_WithTheFirstInstantOfTheCalendarAsABound_BehavesAsAnOrdinaryInstant()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var fromTheStart = await StatementAsync(accountId, "from=0001-01-01T00:00:00Z");
        using var untilTheStart = await StatementAsync(accountId, "to=0001-01-01T00:00:00Z");
        using var theWholeLine = await StatementAsync(accountId, "from=0001-01-01T00:00:00Z&to=2026-03-12T00:00:00-03:00");
        using var roundTripFormat = await StatementAsync(accountId, "from=0001-01-01T00:00:00.0000000%2B00:00");
        using var sameAtBothEnds = await StatementAsync(accountId, "from=0001-01-01T00:00:00Z&to=0001-01-01T00:00:00Z");

        Versions(fromTheStart).ShouldBe([7, 6, 5, 4, 3, 2, 1]);
        Versions(untilTheStart).ShouldBeEmpty();
        Versions(theWholeLine).ShouldBe([7, 6, 5, 4, 3, 2, 1]);
        Versions(roundTripFormat).ShouldBe([7, 6, 5, 4, 3, 2, 1]);
        sameAtBothEnds.ShouldBeProblemCode(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        sameAtBothEnds.Errors().ShouldHaveSingleItem().Reason.ShouldBe("FROM_AFTER_TO");
    }

    [DockerFact]
    public async Task Statement_WithBoundsThatCarryZeroPaddingBeyondTheSixthDecimal_ReturnsTheItemsOfTheSixDigitWindow()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var padded = await StatementAsync(
            accountId,
            "from=2026-03-10T00:00:00.0000000-03:00&to=2026-03-11T03:00:00.000000000Z");

        padded.Status.ShouldBe(HttpStatusCode.OK, padded.Body);
        Versions(padded).ShouldBe([5, 4, 3, 2]);
    }

    [DockerFact]
    public async Task Statement_WithFromAndToTheSameInstantInDifferentOffsets_ReportsFromAfterTo()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var response = await StatementAsync(accountId, "from=2026-03-10T03:00:00Z&to=2026-03-10T00:00:00-03:00");

        response.ShouldBeProblemCode(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        response.Errors().ShouldHaveSingleItem().ShouldBe(("from", "FROM_AFTER_TO", "O campo 'from' deve ser anterior a 'to'."));
    }

    [DockerFact]
    public async Task Statement_WithFromBeforeToInUtcButAfterItOnTheWallClock_IsValid()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var response = await StatementAsync(accountId, "from=2026-03-10T12:00:00%2B14:00&to=2026-03-10T00:00:00Z");

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        Versions(response).ShouldBeEmpty();
    }

    [DockerFact]
    public async Task Statement_WithFromAfterToInUtcButBeforeItOnTheWallClock_ReportsFromAfterTo()
    {
        var accountId = await SeedAroundTheMidnightOfBrasiliaAsync();

        using var response = await StatementAsync(accountId, "from=2026-03-10T00:00:00-03:00&to=2026-03-10T01:00:00Z");

        response.ShouldBeProblemCode(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        response.Errors().ShouldHaveSingleItem().Reason.ShouldBe("FROM_AFTER_TO");
    }

    private async Task<AccountId> SeedAroundTheMidnightOfBrasiliaAsync()
    {
        var accountId = await _world.Host.CreateAccountAsync();

        await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 100.00m, 100.00m, At2330OfThe9th);
        await _world.Seeder.InsertAtAsync(accountId, 2, "CREDIT", 10.00m, 110.00m, MidnightOfThe10th);
        await _world.Seeder.InsertAtAsync(accountId, 3, "DEBIT", 30.00m, 80.00m, At0030OfThe10th);
        await _world.Seeder.InsertAtAsync(accountId, 4, "CREDIT", 20.00m, 100.00m, At2330OfThe10th);
        await _world.Seeder.InsertAtAsync(accountId, 5, "DEBIT", 5.00m, 95.00m, LastMicrosecondOfThe10th);
        await _world.Seeder.InsertAtAsync(accountId, 6, "CREDIT", 50.00m, 145.00m, MidnightOfThe11th);
        await _world.Seeder.InsertAtAsync(accountId, 7, "DEBIT", 45.00m, 100.00m, At0030OfThe11th);

        return accountId;
    }

    private Task<ReadResponse> StatementAsync(AccountId accountId, string query) =>
        _world.Client.StatementAsync(accountId, query);

    private static List<long> Versions(ReadResponse response) =>
        [.. StatementItem.ItemsOf(response).Select(item => item.AccountVersion)];

    private static List<DateTimeOffset> Recorded(ReadResponse response) =>
        [.. StatementItem.ItemsOf(response).Select(item => item.RecordedAtInstant)];
}
