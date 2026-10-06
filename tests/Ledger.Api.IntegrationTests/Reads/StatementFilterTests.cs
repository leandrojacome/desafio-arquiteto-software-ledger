using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class StatementFilterTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task Statement_WithFromInclusiveAndToExclusive_ReturnsTheHalfOpenWindow()
    {
        var accountId = await SeedFourEntriesAsync();

        using var response = await _world.Client.StatementAsync(
            accountId,
            $"from={Encode(Open)}&to={Encode(Open.AddMinutes(5).AddTicks(10))}");

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        Versions(response).ShouldBe([2, 1]);
    }

    [DockerFact]
    public async Task Statement_WithToEqualToTheInstantOfAnEntry_ExcludesThatEntry()
    {
        var accountId = await SeedFourEntriesAsync();

        using var response = await _world.Client.StatementAsync(accountId, $"to={Encode(Open.AddMinutes(5))}");

        Versions(response).ShouldBe([1]);
    }

    [DockerFact]
    public async Task Statement_WithFromEqualToTheInstantOfAnEntry_IncludesThatEntry()
    {
        var accountId = await SeedFourEntriesAsync();

        using var response = await _world.Client.StatementAsync(accountId, $"from={Encode(Open.AddMinutes(5))}");

        Versions(response).ShouldBe([4, 3, 2]);
    }

    [DockerFact]
    public async Task Statement_WithOnlyFromOrOnlyTo_LeavesTheOtherSideOpen()
    {
        var accountId = await SeedFourEntriesAsync();

        using var onlyFrom = await _world.Client.StatementAsync(accountId, $"from={Encode(Open.AddMinutes(5).AddTicks(10))}");
        using var onlyTo = await _world.Client.StatementAsync(accountId, $"to={Encode(Open.AddMinutes(5).AddTicks(10))}");

        Versions(onlyFrom).ShouldBe([4, 3]);
        Versions(onlyTo).ShouldBe([2, 1]);
    }

    [DockerFact]
    public async Task Statement_WithABoundInThePlusZeroZeroForm_IsAccepted()
    {
        var accountId = await SeedFourEntriesAsync();

        using var response = await _world.Client.StatementAsync(
            accountId,
            "from=2026-01-15T10:00:00%2B00:00&to=2026-01-15T10:05:00.000001%2B00:00");

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        Versions(response).ShouldBe([2, 1]);
    }

    [DockerFact]
    public async Task Statement_WithBoundsInTheBrasiliaOffset_ReturnsTheWindowOfTheEquivalentUtcBounds()
    {
        var accountId = await SeedFourEntriesAsync();

        using var local = await _world.Client.StatementAsync(
            accountId,
            "from=2026-01-15T07:00:00-03:00&to=2026-01-15T07:05:00.000001-03:00");
        using var utc = await _world.Client.StatementAsync(
            accountId,
            "from=2026-01-15T10:00:00Z&to=2026-01-15T10:05:00.000001Z");

        local.Status.ShouldBe(HttpStatusCode.OK, local.Body);
        Versions(local).ShouldBe([2, 1]);
        Versions(local).ShouldBe(Versions(utc));
        StatementItem.ItemsOf(local).Select(item => item.RecordedAt).ShouldBe(StatementItem.ItemsOf(utc).Select(item => item.RecordedAt));
    }

    [DockerFact]
    public async Task Statement_WithAWindowWithoutEntries_IsEmptyAndHasNoCursor()
    {
        var accountId = await SeedFourEntriesAsync();

        using var response = await _world.Client.StatementAsync(
            accountId,
            $"from={Encode(Open.AddMinutes(10))}&to={Encode(Open.AddMinutes(20))}");

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        Versions(response).ShouldBeEmpty();
        response.IsNull("nextCursor").ShouldBeTrue();
    }

    [DockerFact]
    public async Task Statement_ContinuedWithAToEarlierThanTheCursor_ResumesFromThePositionWithTheNewFilter()
    {
        var accountId = await SeedFourEntriesAsync();

        using var first = await _world.Client.StatementAsync(accountId, "limit=1");
        var cursor = first.Text("nextCursor");
        using var second = await _world.Client.StatementAsync(
            accountId,
            $"limit=1&to={Encode(Open.AddMinutes(5).AddTicks(10))}&cursor={Uri.EscapeDataString(cursor)}");

        Versions(first).ShouldBe([4]);
        Versions(second).ShouldBe([2]);
    }

    [DockerFact]
    public async Task Statement_ContinuedWithAToLaterThanTheCursor_KeepsThePositionOfTheCursor()
    {
        var accountId = await SeedFourEntriesAsync();

        using var first = await _world.Client.StatementAsync(accountId, "limit=2");
        var cursor = first.Text("nextCursor");
        using var second = await _world.Client.StatementAsync(
            accountId,
            $"limit=5&to={Encode(Open.AddHours(5))}&cursor={Uri.EscapeDataString(cursor)}");

        Versions(first).ShouldBe([4, 3]);
        Versions(second).ShouldBe([2, 1]);
    }

    [DockerFact]
    public async Task Statement_WithToEqualToTheInstantOfTheCursorEntry_DoesNotRepeatTheEntryAlreadyDelivered()
    {
        var accountId = await SeedFourEntriesAsync();

        using var first = await _world.Client.StatementAsync(accountId, "limit=3");
        var cursor = first.Text("nextCursor");
        using var second = await _world.Client.StatementAsync(
            accountId,
            $"to={Encode(Open.AddMinutes(5))}&cursor={Uri.EscapeDataString(cursor)}");

        Versions(first).ShouldBe([4, 3, 2]);
        Versions(second).ShouldBe([1]);
    }

    private async Task<AccountId> SeedFourEntriesAsync()
    {
        var accountId = await _world.Host.CreateAccountAsync();

        await _world.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 100.00m, 100.00m, Open);
        await _world.Seeder.InsertAtAsync(accountId, 2, "DEBIT", 30.00m, 70.00m, Open.AddMinutes(5));
        await _world.Seeder.InsertAtAsync(accountId, 3, "CREDIT", 50.00m, 120.00m, Open.AddMinutes(5).AddTicks(10));
        await _world.Seeder.InsertAtAsync(accountId, 4, "DEBIT", 20.00m, 100.00m, Open.AddHours(1));

        return accountId;
    }

    private static string Encode(DateTimeOffset instant) => Uri.EscapeDataString(StatementItem.FormatInstant(instant));

    private static List<long> Versions(ReadResponse response) =>
        [.. StatementItem.ItemsOf(response).Select(item => item.AccountVersion)];
}
