using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Reads;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.TimeZones;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class BalanceTimeZoneTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string GuidanceFragment = "fuso horário";

    private static readonly DateTimeOffset Open = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

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
    public async Task Balance_AsOfInTheBrasiliaOffset_ReturnsTheSameBalanceAsTheUtcEquivalent()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        var first = await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 500.00m, 500.00m, Open);
        var second = await _world.Seeder.InsertAtAsync(accountId, 2, "DEBIT", 150.00m, 350.00m, Open.AddMinutes(5));

        using var utc = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-15T10:05:00Z");
        using var brasilia = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-15T07:05:00-03:00");
        using var zeroOffset = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-15T10:05:00+00:00");
        using var india = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-15T15:35:00+05:30");
        using var farWest = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-14T20:05:00-14:00");
        using var farEast = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-16T00:05:00+14:00");

        utc.Status.ShouldBe(HttpStatusCode.OK, utc.Body);
        utc.Text("balance").ShouldBe("350.00");
        utc.Text("lastEntryId").ShouldBe(second.ToString());

        foreach (var response in new[] { brasilia, zeroOffset, india, farWest, farEast })
        {
            response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
            response.Body.ShouldBe(utc.Body);
        }

        using var oneSecondBefore = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-15T07:04:59-03:00");

        oneSecondBefore.Text("balance").ShouldBe("500.00");
        oneSecondBefore.Text("lastEntryId").ShouldBe(first.ToString());
    }

    [DockerFact]
    public async Task Balance_AsOfInTheBrasiliaOffset_AnswersTheAsOfInUtcWithZ()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 500.00m, 500.00m, Open);

        using var response = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-15T07:05:00.482913-03:00");

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Text("asOf").ShouldBe("2026-01-15T10:05:00.482913Z");
        response.Body.ShouldNotContain("-03:00");
        response.Body.ShouldNotContain("07:05");
    }

    [DockerFact]
    public async Task Balance_AsOfOnTheBrasiliaWallClock_IsNotTheSameInstantAsTheSameWallClockInUtc()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 500.00m, 500.00m, Open);
        await _world.Seeder.InsertAtAsync(accountId, 2, "DEBIT", 150.00m, 350.00m, Open.AddMinutes(5));
        var third = await _world.Seeder.InsertAtAsync(accountId, 3, "CREDIT", 25.00m, 375.00m, Open.AddHours(2));

        using var inUtc = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-15T10:05:00Z");
        using var inBrasilia = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-15T10:05:00-03:00");

        inUtc.Text("balance").ShouldBe("350.00");
        inBrasilia.Text("balance").ShouldBe("375.00");
        inBrasilia.Text("lastEntryId").ShouldBe(third.ToString());
        inBrasilia.Text("asOf").ShouldBe("2026-01-15T13:05:00.000000Z");
    }

    [DockerFact]
    public async Task Balance_AsOfTheInstantOfTheCurrentBalanceWrittenInBrasilia_ReturnsThatInstantInUtc()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(40.00m);

        using var current = await _world.Client.BalanceAsync(accountId);
        var recordedAt = StatementItem.ParseInstant(current.Text("asOf"));
        using var historical = await _world.Client.BalanceAsOfAsync(accountId, TimeZoneAssertions.Spell(recordedAt, Brasilia));

        current.Status.ShouldBe(HttpStatusCode.OK, current.Body);
        historical.Status.ShouldBe(HttpStatusCode.OK, historical.Body);
        historical.Text("balance").ShouldBe("40.00");
        historical.Text("asOf").ShouldBe(current.Text("asOf"));
    }

    [DockerFact]
    public async Task Balance_AsOfWithoutTimeZone_Returns400WithTheGuidanceToInformTheTimeZone()
    {
        var accountId = await _world.Host.CreateAccountAsync();

        using var response = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-15T10:05:00");

        response.ShouldBeProblemCode(HttpStatusCode.BadRequest, "INVALID_AS_OF");

        var detail = response.Text("detail");

        detail.ShouldContain(GuidanceFragment);
        detail.ShouldContain("'Z'");
        detail.ShouldContain("'-03:00'");
        detail.ShouldContain("ISO 8601");
        detail.ShouldEndWith(".");
        response.Has("errors").ShouldBeFalse();
        response.Header("Cache-Control").ShouldBe("no-store");
        response.Body.ShouldNotContain("10:05:00");
    }

    [DockerTheory]
    [InlineData("2026-01-15T10:05:00+25:00")]
    [InlineData("2026-01-15T10:05:00-15:00")]
    [InlineData("2026-01-15T10:05:00+14:01")]
    [InlineData("2026-01-15T10:05:00-00:00")]
    [InlineData("2026-01-15T10:05:00+0300")]
    [InlineData("2026-01-15T10:05:00.1234567-03:00")]
    [InlineData("2026-01-15T10:05-03:00")]
    public async Task Balance_AsOfWithAnInvalidOffsetOrShape_Returns400InvalidAsOf(string asOf)
    {
        var accountId = await _world.Host.CreateAccountAsync();

        using var response = await _world.Client.BalanceAsOfAsync(accountId, asOf);

        response.ShouldBeProblemCode(HttpStatusCode.BadRequest, "INVALID_AS_OF");
        response.Text("detail").ShouldContain(GuidanceFragment);
        response.Has("errors").ShouldBeFalse();
        response.Body.ShouldNotContain("25:00");
        response.Body.ShouldNotContain("10:05");
    }

    [DockerFact]
    public async Task Balance_AsOfWithAPositiveOffsetAndTheRawPlus_IsRefusedAndWithPercentTwoBIsAccepted()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 500.00m, 500.00m, Open);

        using var raw = await _world.Client.GetAsync($"{ReadApiClient.BalancePath(accountId)}?asOf=2026-01-15T15:35:00+05:30");
        using var escaped = await _world.Client.GetAsync($"{ReadApiClient.BalancePath(accountId)}?asOf=2026-01-15T15:35:00%2B05:30");

        raw.ShouldBeProblemCode(HttpStatusCode.BadRequest, "INVALID_AS_OF");
        escaped.Status.ShouldBe(HttpStatusCode.OK, escaped.Body);
        escaped.Text("balance").ShouldBe("500.00");
    }

    [DockerFact]
    public async Task Balance_AsOfAheadOfTheDatabase_IsRefusedInUtcWhateverTheOffsetOfTheText()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        var databaseNow = new DateTimeOffset(await _world.Queries.DatabaseNowAsync(), TimeSpan.Zero);
        var ahead = databaseNow.AddHours(1);

        foreach (var offset in new[] { Brasilia, TimeSpan.Zero, TimeSpan.FromHours(-14), TimeSpan.FromHours(14) })
        {
            using var response = await _world.Client.BalanceAsOfAsync(accountId, TimeZoneAssertions.Spell(ahead, offset));

            response.ShouldBeProblemCode(HttpStatusCode.BadRequest, "INVALID_AS_OF");
            response.Text("detail").ShouldContain("posterior");
        }
    }

    [DockerFact]
    public async Task Balance_AsOfInThePastIsAcceptedWhateverTheWallClockOfTheOffset()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        var databaseNow = new DateTimeOffset(await _world.Queries.DatabaseNowAsync(), TimeSpan.Zero);
        var past = databaseNow.AddHours(-1);

        foreach (var offset in new[] { Brasilia, TimeSpan.Zero, TimeSpan.FromHours(-14), TimeSpan.FromHours(14) })
        {
            using var response = await _world.Client.BalanceAsOfAsync(accountId, TimeZoneAssertions.Spell(past, offset));

            response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
            response.Text("asOf").ShouldBe(TimeZoneAssertions.SpellInUtc(past));
        }
    }

    [DockerTheory]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("0001-01-01T00:00:00+00:00")]
    [InlineData("0001-01-01T00:00:00.0000000+00:00")]
    public async Task Balance_AsOfTheFirstInstantOfTheCalendar_IsZeroAndNeverTheBalanceOfToday(string asOf)
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 500.00m, 500.00m, Open);

        using var response = await _world.Client.BalanceAsOfAsync(accountId, asOf);

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Text("balance").ShouldBe("0.00");
        response.Text("asOf").ShouldBe("0001-01-01T00:00:00.000000Z");
        response.IsNull("lastEntryId").ShouldBeTrue();
        response.Json.GetProperty("settled").GetBoolean().ShouldBeTrue();
    }

    [DockerTheory]
    [InlineData("2026-01-15T07:05:00.0000000-03:00")]
    [InlineData("2026-01-15T10:05:00.000000000Z")]
    public async Task Balance_AsOfWithZeroPaddingBeyondTheSixthDecimal_ReturnsTheSameBalanceAsTheSixDigitSpelling(string asOf)
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 500.00m, 500.00m, Open);
        await _world.Seeder.InsertAtAsync(accountId, 2, "DEBIT", 150.00m, 350.00m, Open.AddMinutes(5));

        using var response = await _world.Client.BalanceAsOfAsync(accountId, asOf);
        using var expected = await _world.Client.BalanceAsOfAsync(accountId, "2026-01-15T10:05:00.000000Z");

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Body.ShouldBe(expected.Body);
        response.Text("balance").ShouldBe("350.00");
    }

    [DockerFact]
    public async Task Balance_AsOfOfAnUnknownAccountWithTheBrasiliaOffset_Returns404()
    {
        var unknown = AccountId.From(Guid.CreateVersion7()).Value;

        using var response = await _world.Client.BalanceAsOfAsync(unknown, "2026-01-15T07:05:00-03:00");

        response.ShouldBeProblemCode(HttpStatusCode.NotFound, "ACCOUNT_NOT_FOUND");
    }
}
