using System.Globalization;
using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class AsOfBalanceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Open = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

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
    public async Task Balance_AsOfTheSecondBeforeAndTheExactInstantOfAnEntry_FollowsTheInclusiveBoundary()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        var first = await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 500.00m, 500.00m, Open);
        var second = await _world.Seeder.InsertAtAsync(accountId, 2, "DEBIT", 150.00m, 350.00m, Open.AddMinutes(5));

        using var before = await _world.Client.BalanceAtAsync(accountId, Open.AddMinutes(5).AddSeconds(-1));
        using var exact = await _world.Client.BalanceAtAsync(accountId, Open.AddMinutes(5));

        before.Status.ShouldBe(HttpStatusCode.OK, before.Body);
        before.Text("balance").ShouldBe("500.00");
        before.Text("lastEntryId").ShouldBe(first.ToString());
        exact.Status.ShouldBe(HttpStatusCode.OK, exact.Body);
        exact.Text("balance").ShouldBe("350.00");
        exact.Text("lastEntryId").ShouldBe(second.ToString());
        exact.Text("asOf").ShouldBe("2026-01-15T10:05:00.000000Z");
        exact.Json.GetProperty("settled").GetBoolean().ShouldBeTrue();
    }

    [DockerFact]
    public async Task Balance_AsOfInstantsOneMicrosecondApart_DoesNotRound()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        var instant = Open.AddMinutes(5);
        await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 100.00m, 100.00m, instant);
        await _world.Seeder.InsertAtAsync(accountId, 2, "CREDIT", 50.00m, 150.00m, instant.AddTicks(10));

        using var atFirst = await _world.Client.BalanceAtAsync(accountId, instant);
        using var atSecond = await _world.Client.BalanceAtAsync(accountId, instant.AddTicks(10));
        using var justBefore = await _world.Client.BalanceAtAsync(accountId, instant.AddTicks(-10));

        atFirst.Text("balance").ShouldBe("100.00");
        atSecond.Text("balance").ShouldBe("150.00");
        atSecond.Text("asOf").ShouldBe("2026-01-15T10:05:00.000001Z");
        justBefore.Text("balance").ShouldBe("0.00");
        justBefore.IsNull("lastEntryId").ShouldBeTrue();
    }

    [DockerFact]
    public async Task Balance_AsOfBeforeTheFirstEntryAndBeforeTheAccountExisted_IsZeroWithoutALastEntry()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 500.00m, 500.00m, Open);

        using var beforeTheEntry = await _world.Client.BalanceAtAsync(accountId, Open.AddSeconds(-1));
        using var beforeTheAccount = await _world.Client.BalanceAsOfAsync(accountId, "2001-01-01T00:00:00Z");

        foreach (var response in new[] { beforeTheEntry, beforeTheAccount })
        {
            response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
            response.Text("balance").ShouldBe("0.00");
            response.IsNull("lastEntryId").ShouldBeTrue();
            response.Json.GetProperty("settled").GetBoolean().ShouldBeTrue();
        }
    }

    [DockerFact]
    public async Task Balance_AsOfYesterdayEvening_IgnoresTheBusinessDateOfAnEntryRecordedNow()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        var yesterdayEvening = new DateTimeOffset(ReadClock.UtcNow.Date.AddDays(-1).AddHours(18), TimeSpan.Zero);
        await _world.Host.RegisterAsync(accountId, "late-entry", EntryType.Credit, 100.00m, occurredAt: yesterdayEvening);

        using var then = await _world.Client.BalanceAtAsync(accountId, yesterdayEvening);
        using var now = await _world.Client.BalanceAsync(accountId);

        then.Text("balance").ShouldBe("0.00");
        then.IsNull("lastEntryId").ShouldBeTrue();
        now.Text("balance").ShouldBe("100.00");
    }

    [DockerFact]
    public async Task Balance_AsOfInTheZuluTheZeroOffsetAndTheBrasiliaForms_ReturnsTheSameBodyWithSixFractionDigits()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        var instant = new DateTimeOffset(ReadClock.UtcNow.Date.AddDays(-2).AddHours(14).AddMinutes(3).AddSeconds(11), TimeSpan.Zero);
        var text = instant.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var brasilia = instant.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 920.00m, 920.00m, instant.AddHours(-1));

        using var zulu = await _world.Client.BalanceAsOfAsync(accountId, text + "Z");
        using var offset = await _world.Client.BalanceAsOfAsync(accountId, text + "+00:00");
        using var local = await _world.Client.BalanceAsOfAsync(accountId, brasilia);

        zulu.Status.ShouldBe(HttpStatusCode.OK, zulu.Body);
        zulu.Text("asOf").ShouldBe(text + ".000000Z");
        zulu.Text("balance").ShouldBe("920.00");
        offset.Body.ShouldBe(zulu.Body);
        local.Body.ShouldBe(zulu.Body);
    }

    [DockerFact]
    public async Task Balance_AsOf_ReportsTheOverdraftLimitOfToday()
    {
        var accountId = await _world.Host.CreateAccountAsync(overdraftLimit: 10.00m);
        await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 40.00m, 40.00m, Open);
        await _world.Ledger.SetOverdraftLimitAsync(accountId, 75.00m);

        using var historical = await _world.Client.BalanceAtAsync(accountId, Open.AddHours(1));

        historical.Status.ShouldBe(HttpStatusCode.OK, historical.Body);
        historical.Text("overdraftLimit").ShouldBe("75.00");
        historical.Text("balance").ShouldBe("40.00");
    }

    [DockerFact]
    public async Task Balance_AsOfOfAnUnknownAccount_Returns404()
    {
        var unknown = AccountId.From(Guid.CreateVersion7()).Value;

        using var response = await _world.Client.BalanceAtAsync(unknown, Open);

        response.ShouldBeProblem(HttpStatusCode.NotFound, "ACCOUNT_NOT_FOUND", "Conta não encontrada");
    }

    [DockerFact]
    public async Task Balance_AsOfRepeatedWhileNewEntriesArrive_IsStable()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Ledger.SeedChainAsync(accountId, 5, Open, TimeSpan.FromMinutes(1));

        using var first = await _world.Client.BalanceAtAsync(accountId, Open.AddHours(1));
        await _world.Host.RegisterAsync(accountId, "later", EntryType.Credit, 25.00m);
        using var second = await _world.Client.BalanceAtAsync(accountId, Open.AddHours(1));

        second.Body.ShouldBe(first.Body);
    }
}
