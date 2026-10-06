using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Api.IntegrationTests.Concurrency;

internal static class ConcurrencyScenarios
{
    public const int RandomSeed = 20261001;

    public static async Task AssertConsistentAsync(PostgresFixture postgres, AccountId accountId)
    {
        await InvariantVerifier.AssertAccountIsConsistentAsync(
            postgres.AdministrativeSource,
            accountId.Value,
            CancellationToken.None);
    }

    [SuppressMessage("Security", "CA5394",
        Justification = "A seeded sequence makes the test data reproducible; nothing here is security sensitive.")]
    public static List<(EntryType Type, decimal Amount)> MixedRequests(int count, int seed)
    {
        var random = new Random(seed);
        var requests = new List<(EntryType, decimal)>();

        for (var index = 0; index < count; index++)
        {
            var type = random.Next(2) == 0 ? EntryType.Credit : EntryType.Debit;
            var amount = random.Next(1, 50_001) / 100m;

            requests.Add((type, amount));
        }

        return requests;
    }

    public static decimal SignedSum(IEnumerable<(EntryType Type, decimal Amount)> requests) =>
        requests.Sum(request => request.Type == EntryType.Credit ? request.Amount : -request.Amount);

    public static bool Refused(Result<EntryOutcome> result, Error error) => result.IsFailure && result.Error == error;
}
