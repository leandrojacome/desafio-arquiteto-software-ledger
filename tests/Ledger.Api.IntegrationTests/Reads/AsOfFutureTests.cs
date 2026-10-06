using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class AsOfFutureTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Title = "Instante 'asOf' inválido";
    private const string Detail = "O parâmetro 'asOf' não pode ser posterior ao instante atual do ledger.";

    private ReadWorld? _created;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_created is not null)
        {
            await _created.DisposeAsync();
        }
    }

    private ReadWorld Default => _created ?? World(TimeSpan.Zero);

    private ReadWorld World(TimeSpan apiClockSkew)
    {
        var apiClock = new FakeTimeProvider(ReadClock.UtcNow + apiClockSkew);

        _created = ReadWorld.Create(
            postgres,
            configureServices: services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(apiClock);
            });

        return _created;
    }

    [DockerFact]
    public async Task Balance_AsOfOneMinuteAheadOfTheDatabase_Returns400InvalidAsOf()
    {
        var accountId = await Default.Host.CreateAccountAsync();
        var databaseNow = await DatabaseNowAsync();

        using var response = await Default.Client.BalanceAtAsync(accountId, databaseNow.AddMinutes(1));

        response.ShouldBeProblem(HttpStatusCode.BadRequest, "INVALID_AS_OF", Title, ReadApiClient.BalancePath(accountId));
        response.Text("detail").ShouldBe(Detail);
        response.Has("errors").ShouldBeFalse();
        response.Header("Cache-Control").ShouldBe("no-store");
    }

    [DockerFact]
    public async Task Balance_AsOfTheInstantTheCurrentBalanceJustReturned_Returns200()
    {
        var accountId = await Default.Host.CreateFundedAccountAsync(10.00m);

        using var current = await Default.Client.BalanceAsync(accountId);
        using var historical = await Default.Client.BalanceAsOfAsync(accountId, current.Text("asOf"));

        current.Status.ShouldBe(HttpStatusCode.OK, current.Body);
        historical.Status.ShouldBe(HttpStatusCode.OK, historical.Body);
        historical.Text("balance").ShouldBe("10.00");
    }

    [DockerFact]
    public async Task Balance_WithTheApiClockAnHourBehindTheDatabase_AcceptsTheRecordedAtOfAFreshEntry()
    {
        var world = World(TimeSpan.FromHours(-1));
        var accountId = await world.Host.CreateAccountAsync();
        var written = await world.Host.RegisterAsync(accountId, "fresh", EntryType.Credit, 20.00m);

        using var response = await world.Client.BalanceAtAsync(accountId, written.Value.Entry.RecordedAt);

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Text("balance").ShouldBe("20.00");
        response.Text("lastEntryId").ShouldBe(written.Value.Entry.Id.ToString());
    }

    [DockerFact]
    public async Task Balance_WithTheApiClockAnHourAheadOfTheDatabase_StillRefusesAnInstantAheadOfTheDatabase()
    {
        var world = World(TimeSpan.FromHours(1));
        var accountId = await world.Host.CreateAccountAsync();
        var databaseNow = await DatabaseNowAsync();

        using var response = await world.Client.BalanceAtAsync(accountId, databaseNow.AddMinutes(10));

        response.ShouldBeProblem(HttpStatusCode.BadRequest, "INVALID_AS_OF", Title);
    }

    [DockerFact]
    public async Task Balance_AsOfOfAnEntryRecordedAnHourAheadOfTheDatabase_IsRefusedWhileTheCurrentBalanceIncludesIt()
    {
        var accountId = await Default.Host.CreateAccountAsync();
        var ahead = (await DatabaseNowAsync()).AddHours(1);
        var entryId = await Default.Seeder.InsertAtAsync(accountId, 1, "CREDIT", 10.00m, 10.00m, ahead);
        await Default.Ledger.SetCurrentBalanceAsync(accountId, 10.00m, 1, entryId, ahead);

        using var historical = await Default.Client.BalanceAtAsync(accountId, ahead);
        using var current = await Default.Client.BalanceAsync(accountId);

        historical.ShouldBeProblem(HttpStatusCode.BadRequest, "INVALID_AS_OF", Title);
        current.Status.ShouldBe(HttpStatusCode.OK, current.Body);
        current.Text("balance").ShouldBe("10.00");
        current.Text("lastEntryId").ShouldBe(entryId.ToString());
    }

    [DockerFact]
    public async Task Balance_OfAnUnknownAccountAsOfTheFuture_Returns404BeforeTheFutureCheck()
    {
        var unknown = AccountId.From(Guid.CreateVersion7()).Value;
        var databaseNow = await DatabaseNowAsync();

        using var response = await Default.Client.BalanceAtAsync(unknown, databaseNow.AddDays(1));

        response.ShouldBeProblem(HttpStatusCode.NotFound, "ACCOUNT_NOT_FOUND", "Conta não encontrada");
    }

    private async Task<DateTimeOffset> DatabaseNowAsync() =>
        new(await Default.Queries.DatabaseNowAsync(), TimeSpan.Zero);
}
