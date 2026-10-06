using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Entries;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Persistence;

namespace Ledger.Api.IntegrationTests.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class SaturatedWritePoolTests(PostgresFixture postgres)
{
    private const int Calls = 100;
    private const decimal OpeningBalance = 500.00m;
    private const decimal DebitAmount = 10.00m;

    [DockerFact]
    public async Task DebitsOnTheDefaultPool_AnswerOnlyAcceptedRefusedOrUnavailable_AndNeverLeaveAPartialWrite()
    {
        await using var host = LedgerHost.Create(postgres);

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var queries = new LedgerQueries(postgres);
            var classifier = new PostgresTransientFailureClassifier();
            var accountId = await host.CreateFundedAccountAsync(OpeningBalance);

            var outcomes = await ParallelGate.RunAsync(
                Calls,
                index => AttemptAsync(host, accountId, $"saturated-{index}"));

            var accepted = outcomes.Count(outcome => outcome.Result?.IsSuccess == true);
            var refused = outcomes.Count(outcome =>
                outcome.Result is { } result && ConcurrencyScenarios.Refused(result, EntryErrors.InsufficientFunds));
            var unavailable = outcomes.Count(outcome => outcome.Failure is { } failure && classifier.IsTransient(failure));

            outcomes.Count(outcome => outcome.Failure is { } failure && !classifier.IsTransient(failure)).ShouldBe(0);
            (accepted + refused + unavailable).ShouldBe(Calls);
            (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(OpeningBalance - (DebitAmount * accepted));
            (await queries.CountEntriesAsync(accountId)).ShouldBe(accepted + 1);
            (await queries.CountOutboxAsync(accountId)).ShouldBe(accepted + 1);
            (await queries.CountKeysAsync(accountId)).ShouldBe(accepted + 1);
            await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
        });
    }

    private static async Task<Attempt> AttemptAsync(LedgerHost host, Ledger.Domain.Accounts.AccountId accountId, string key)
    {
        try
        {
            return new Attempt(await host.RegisterAsync(accountId, key, EntryType.Debit, DebitAmount), null);
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException)
        {
            return new Attempt(null, exception);
        }
    }

    private sealed record Attempt(Result<EntryOutcome>? Result, Exception? Failure);
}
