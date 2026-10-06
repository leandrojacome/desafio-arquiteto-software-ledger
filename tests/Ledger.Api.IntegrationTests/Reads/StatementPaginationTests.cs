using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class StatementPaginationTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task Statement_Of120EntriesReadFiftyAtATime_ReturnsThreePagesWithoutRepeatingOrSkipping()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Ledger.SeedChainAsync(accountId, 120, Open, TimeSpan.FromSeconds(1));

        var walk = await _world.Client.WalkAsync(accountId, 50);

        walk.PageSizes.ShouldBe([50, 50, 20]);
        walk.Cursors[0].ShouldNotBeNull();
        walk.Cursors[1].ShouldNotBeNull();
        walk.Cursors[2].ShouldBeNull();
        walk.Items.Select(item => item.AccountVersion).ShouldBe(Enumerable.Range(1, 120).Select(version => (long)version).Reverse());
        walk.Items.Select(item => item.RecordedAtInstant).ShouldBe([.. walk.Items.Select(item => item.RecordedAtInstant).OrderDescending()]);
    }

    [DockerFact]
    public async Task Statement_WithoutALimit_ReturnsTheDefaultPageOfFiftyAndEchoesTheLimit()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Ledger.SeedChainAsync(accountId, 60, Open, TimeSpan.FromSeconds(1));

        using var response = await _world.Client.StatementAsync(accountId);

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        StatementItem.ItemsOf(response).Count.ShouldBe(50);
        response.Json.GetProperty("limit").GetInt32().ShouldBe(50);
        response.IsNull("nextCursor").ShouldBeFalse();
        response.ContentType.ShouldBe("application/json");
        response.Header("Cache-Control").ShouldBe("no-store");
    }

    [DockerFact]
    public async Task Statement_WithTheLimitsOfTheContract_AcceptsOneAndTwoHundredAndRefusesTwoHundredAndOne()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Ledger.SeedChainAsync(accountId, 120, Open, TimeSpan.FromSeconds(1));

        using var one = await _world.Client.StatementAsync(accountId, "limit=1");
        using var twoHundred = await _world.Client.StatementAsync(accountId, "limit=200");
        using var tooMany = await _world.Client.StatementAsync(accountId, "limit=201");

        StatementItem.ItemsOf(one).ShouldHaveSingleItem().AccountVersion.ShouldBe(120);
        one.IsNull("nextCursor").ShouldBeFalse();
        StatementItem.ItemsOf(twoHundred).Count.ShouldBe(120);
        twoHundred.IsNull("nextCursor").ShouldBeTrue();
        twoHundred.Json.GetProperty("limit").GetInt32().ShouldBe(200);
        tooMany.ShouldBeProblem(HttpStatusCode.BadRequest, "VALIDATION_FAILED", "Falha na validação da requisição");
        tooMany.Errors().ShouldHaveSingleItem().ShouldBe(("limit", "OUT_OF_RANGE", "O campo 'limit' deve estar entre 1 e 200."));
    }

    [DockerFact]
    public async Task Statement_OfExactlyOneHundredEntriesReadFiftyAtATime_EndsOnTheSecondPage()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Ledger.SeedChainAsync(accountId, 100, Open, TimeSpan.FromSeconds(1));

        var walk = await _world.Client.WalkAsync(accountId, 50);

        walk.PageSizes.ShouldBe([50, 50]);
        walk.Cursors[1].ShouldBeNull();
        walk.Items.Count.ShouldBe(100);
    }

    [DockerFact]
    public async Task Statement_WithEntriesSharingTheSameInstant_ReadOneAtATime_KeepsAStableOrderWithoutLoss()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Ledger.SeedChainAsync(accountId, 31, Open, TimeSpan.FromSeconds(1), entriesPerInstant: 3);

        var oneByOne = await _world.Client.WalkAsync(accountId, 1);
        var byTwo = await _world.Client.WalkAsync(accountId, 2);
        var byFour = await _world.Client.WalkAsync(accountId, 4);

        var expected = Enumerable.Range(1, 31).Select(version => (long)version).Reverse().ToList();

        oneByOne.Items.Select(item => item.AccountVersion).ShouldBe(expected);
        byTwo.Items.Select(item => item.AccountVersion).ShouldBe(expected);
        byFour.Items.Select(item => item.AccountVersion).ShouldBe(expected);
        oneByOne.Items.Select(item => item.RecordedAt).Distinct().Count().ShouldBe(11);
    }

    [DockerFact]
    public async Task Statement_FollowedPageByPage_NeverReturnsTheSameCursorTwice()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Ledger.SeedChainAsync(accountId, 40, Open, TimeSpan.FromSeconds(1), entriesPerInstant: 2);

        var walk = await _world.Client.WalkAsync(accountId, 7);
        var cursors = walk.Cursors.OfType<string>().ToList();

        cursors.Distinct().Count().ShouldBe(cursors.Count);
        walk.Items.Count.ShouldBe(40);
        walk.Items.Select(item => item.EntryId).Distinct().Count().ShouldBe(40);
    }
}
