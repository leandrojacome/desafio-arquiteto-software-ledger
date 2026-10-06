using System.Diagnostics.CodeAnalysis;
using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
[SuppressMessage("Security", "CA5394", Justification = "A fixed seed reproduces the sequence; this is test data and not security.")]
public sealed class StatementMatchesBalanceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int Accounts = 50;
    private const int Intervals = 20;
    private const long OneMicrosecond = 10;

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
    public async Task Balance_OfFiftyAccounts_EqualsTheBalanceAfterOfTheLastEntryAndTheChainCloses()
    {
        var random = new Random(20261003);

        for (var index = 0; index < Accounts; index++)
        {
            var accountId = await BuildHistoryAsync(random, index, 5);

            using var balance = await _world.Client.BalanceAsync(accountId);
            using var statement = await _world.Client.StatementAsync(accountId, "limit=200");

            balance.Status.ShouldBe(HttpStatusCode.OK, balance.Body);

            var items = StatementItem.ItemsOf(statement);
            var last = items.Single(item => item.EntryId == balance.Text("lastEntryId"));

            balance.Money("balance").ShouldBe(last.BalanceAfter);
            items[0].EntryId.ShouldBe(last.EntryId);
            CheckTheChain(items);
        }
    }

    [DockerFact]
    public async Task Statement_SumOfAWindow_EqualsTheBalanceBeforeTheEndMinusTheBalanceBeforeTheStart()
    {
        var random = new Random(20261004);
        var accountId = await BuildHistoryAsync(random, 0, 12);

        var entries = await _world.Queries.EntriesAsync(accountId);
        var instants = entries.Select(entry => new DateTimeOffset(entry.RecordedAt, TimeSpan.Zero)).ToList();
        var candidates = instants
            .SelectMany(instant => new[] { instant, instant.AddTicks(-OneMicrosecond), instant.AddTicks(OneMicrosecond) })
            .Append(instants[0].AddMilliseconds(-3))
            .Distinct()
            .Order()
            .ToList();
        var borderCaseWithoutTheAdjustmentFailed = false;

        for (var interval = 0; interval < Intervals; interval++)
        {
            var (from, to) = interval == 0
                ? (instants[0], instants[^1])
                : PickWindow(random, candidates);

            var window = await _world.Client.WalkAsync(
                accountId,
                200,
                StatementItem.FormatInstant(from),
                StatementItem.FormatInstant(to));
            var sum = window.Items.Sum(item => item.SignedAmount);
            var openingBalance = await BalanceAtAsync(accountId, from.AddTicks(-OneMicrosecond));
            var closingBalance = await BalanceAtAsync(accountId, to.AddTicks(-OneMicrosecond));

            sum.ShouldBe(closingBalance - openingBalance, $"window [{from:O}, {to:O})");

            var withoutTheAdjustment = await BalanceAtAsync(accountId, to) - await BalanceAtAsync(accountId, from);

            if (withoutTheAdjustment != sum)
            {
                borderCaseWithoutTheAdjustmentFailed = true;
            }
        }

        borderCaseWithoutTheAdjustmentFailed.ShouldBeTrue();
    }

    [DockerFact]
    public async Task Statement_OpeningBalanceOfTheWindow_IsTheBalanceAfterOfTheOldestItemMinusItsSignedAmount()
    {
        var random = new Random(20261005);
        var accountId = await BuildHistoryAsync(random, 0, 8);
        var entries = await _world.Queries.EntriesAsync(accountId);
        var from = new DateTimeOffset(entries[3].RecordedAt, TimeSpan.Zero);

        using var response = await _world.Client.StatementAsync(accountId, $"from={Uri.EscapeDataString(StatementItem.FormatInstant(from))}&limit=200");

        var items = StatementItem.ItemsOf(response);
        var oldest = items[^1];

        (oldest.BalanceAfter - oldest.SignedAmount).ShouldBe(await BalanceAtAsync(accountId, from.AddTicks(-OneMicrosecond)));
    }

    private static (DateTimeOffset From, DateTimeOffset To) PickWindow(Random random, List<DateTimeOffset> candidates)
    {
        var first = random.Next(candidates.Count - 1);
        var second = random.Next(first + 1, candidates.Count);

        return (candidates[first], candidates[second]);
    }

    private static void CheckTheChain(IReadOnlyList<StatementItem> items)
    {
        for (var index = 0; index < items.Count - 1; index++)
        {
            (items[index].BalanceAfter - items[index].SignedAmount).ShouldBe(items[index + 1].BalanceAfter);
        }

        (items[^1].BalanceAfter - items[^1].SignedAmount).ShouldBe(0.00m);
    }

    private async Task<decimal> BalanceAtAsync(AccountId accountId, DateTimeOffset instant)
    {
        using var response = await _world.Client.BalanceAtAsync(accountId, instant);

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);

        return response.Money("balance");
    }

    private async Task<AccountId> BuildHistoryAsync(Random random, int index, int operations)
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(1000.00m);
        EntryId? first = null;

        for (var step = 0; step < operations; step++)
        {
            var type = random.Next(3) == 0 ? EntryType.Debit : EntryType.Credit;
            var amount = random.Next(100, 20000) / 100m;
            var written = await _world.Host.RegisterAsync(accountId, $"match-{index}-{step}", type, amount);

            written.IsSuccess.ShouldBeTrue();
            first ??= written.Value.Entry.Id;
        }

        if (first is { } original)
        {
            var reversed = await _world.Host.ReverseAsync(accountId, original, $"match-{index}-reversal");

            reversed.IsSuccess.ShouldBeTrue();
        }

        return accountId;
    }
}
