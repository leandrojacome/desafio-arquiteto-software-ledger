using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Entries;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class SettledWindowTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ReadWorld? _created;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_created is not null)
        {
            await _created.DisposeAsync();
        }
    }

    private ReadWorld World(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        _created ??= ReadWorld.Create(postgres, overrides);

        return _created;
    }

    [DockerFact]
    public async Task Balance_AsOfAnEntryFromHoursAgo_IsSettled()
    {
        var world = World();
        var accountId = await world.Host.CreateAccountAsync();
        var recordedAt = new DateTimeOffset(ReadClock.UtcNow.AddHours(-3).UtcDateTime.Date, TimeSpan.Zero).AddHours(1);
        await world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 10.00m, 10.00m, recordedAt);

        using var response = await world.Client.BalanceAtAsync(accountId, recordedAt);

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Json.GetProperty("settled").GetBoolean().ShouldBeTrue();
    }

    [DockerFact]
    public async Task Balance_AsOfTheLastTwoSeconds_IsNotSettled()
    {
        var world = World();
        var accountId = await world.Host.CreateFundedAccountAsync(10.00m);
        var databaseNow = new DateTimeOffset(await world.Queries.DatabaseNowAsync(), TimeSpan.Zero);

        using var response = await world.Client.BalanceAtAsync(accountId, databaseNow.AddSeconds(-2));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Json.GetProperty("settled").GetBoolean().ShouldBeFalse();
    }

    [DockerFact]
    public async Task Balance_CurrentHasNoSettledAndEveryHistoricalHasIt()
    {
        var world = World();
        var accountId = await world.Host.CreateFundedAccountAsync(10.00m);

        using var current = await world.Client.BalanceAsync(accountId);
        using var historical = await world.Client.BalanceAtAsync(accountId, ReadClock.UtcNow.AddDays(-1));

        current.Has("settled").ShouldBeFalse();
        historical.Has("settled").ShouldBeTrue();
        historical.Json.GetProperty("settled").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.True);
    }

    [DockerFact]
    public async Task Balance_WithAOneSecondWindow_SettlesAnInstantTwoSecondsAgo()
    {
        var world = World(new Dictionary<string, string?> { ["Ledger:Balance:SettlingWindowSeconds"] = "1" });
        var accountId = await world.Host.CreateFundedAccountAsync(10.00m);
        var databaseNow = new DateTimeOffset(await world.Queries.DatabaseNowAsync(), TimeSpan.Zero);

        using var response = await world.Client.BalanceAtAsync(accountId, databaseNow.AddSeconds(-2));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Json.GetProperty("settled").GetBoolean().ShouldBeTrue();
    }

    [DockerFact]
    public async Task Balance_WithAnEntryStillInFlight_DoesNotSeeItAndSaysTheInstantIsNotSettled()
    {
        var world = World();
        var accountId = await world.Host.CreateAccountAsync();
        var databaseNow = new DateTimeOffset(await world.Queries.DatabaseNowAsync(), TimeSpan.Zero);
        var asOf = databaseNow.AddSeconds(-1);

        await using var inFlight = await world.Ledger.OpenInFlightEntryAsync(accountId, TimeSpan.FromSeconds(2), 10.00m);

        using var whileOpen = await world.Client.BalanceAtAsync(accountId, asOf);

        await inFlight.CommitAsync();

        using var afterCommit = await world.Client.BalanceAtAsync(accountId, asOf);

        whileOpen.Status.ShouldBe(HttpStatusCode.OK, whileOpen.Body);
        whileOpen.Text("balance").ShouldBe("0.00");
        whileOpen.IsNull("lastEntryId").ShouldBeTrue();
        whileOpen.Json.GetProperty("settled").GetBoolean().ShouldBeFalse();
        afterCommit.Text("balance").ShouldBe("10.00");
        afterCommit.IsNull("lastEntryId").ShouldBeFalse();
        afterCommit.Json.GetProperty("settled").GetBoolean().ShouldBeFalse();
    }

    [DockerFact]
    public async Task Balance_AsOfOutsideTheWindow_ReturnsTheSameAnswerBeforeAndAfterNewWrites()
    {
        var world = World();
        var accountId = await world.Host.CreateAccountAsync();
        var start = ReadClock.UtcNow.AddHours(-3);
        await world.Ledger.SeedChainAsync(accountId, 10, start, TimeSpan.FromMinutes(1));
        var asOf = start.AddMinutes(5).AddTicks(-start.Ticks % 10);

        using var before = await world.Client.BalanceAtAsync(accountId, asOf);
        var written = await world.Host.RegisterAsync(accountId, "after", EntryType.Credit, 1.00m);
        using var after = await world.Client.BalanceAtAsync(accountId, asOf);

        written.IsSuccess.ShouldBeTrue();
        before.Json.GetProperty("settled").GetBoolean().ShouldBeTrue();
        after.Body.ShouldBe(before.Body);
    }
}
