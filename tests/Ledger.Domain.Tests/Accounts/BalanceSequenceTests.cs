using System.Diagnostics.CodeAnalysis;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Tests.Accounts;

[Trait("Category", "Unit")]
public sealed class BalanceSequenceTests
{
    private const int Operations = 10_000;
    private const int Seed = 20261001;
    private const decimal Limit = 200.00m;

    private static readonly AccountId Account = AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;

    [Fact]
    [SuppressMessage("Security", "CA5394",
        Justification = "A fixed seed makes the random sequence reproducible; no security decision depends on it.")]
    public void Apply_OverTenThousandRandomOperations_SumsTheAcceptedOnesAndNeverGoesBelowTheLimit()
    {
        var random = new Random(Seed);
        var balance = AccountBalance.Create(Account, "BRL", 0.00m, Limit).Value;
        var expected = 0.00m;
        var lowest = 0.00m;
        var accepted = 0;
        var refused = 0;

        for (var index = 0; index < Operations; index++)
        {
            var type = random.Next(2) == 0 ? EntryType.Credit : EntryType.Debit;
            var amount = Money.CreatePositive(random.Next(1, 50_001) / 100m, "BRL").Value;
            var delta = type.SignedDelta(amount);
            var fits = expected + delta >= -Limit;

            var result = balance.Apply(type, amount);

            result.IsSuccess.ShouldBe(fits);
            if (result.IsSuccess)
            {
                balance = result.Value;
                expected += delta;
                accepted++;
            }
            else
            {
                result.Error.ShouldBe(EntryErrors.InsufficientFunds);
                refused++;
            }

            balance.Amount.ShouldBe(expected);
            lowest = Math.Min(lowest, balance.Amount);
        }

        balance.Amount.ShouldBe(expected);
        lowest.ShouldBeGreaterThanOrEqualTo(-Limit);
        accepted.ShouldBeGreaterThan(0);
        refused.ShouldBeGreaterThan(0);
        (accepted + refused).ShouldBe(Operations);
    }

    [Fact]
    [SuppressMessage("Security", "CA5394",
        Justification = "A fixed seed makes the random sequence reproducible; no security decision depends on it.")]
    public void Apply_WithTheSameSeedTwice_ProducesTheSameFinalBalance()
    {
        var first = Run(new Random(Seed));
        var second = Run(new Random(Seed));

        first.ShouldBe(second);
    }

    [Fact]
    [SuppressMessage("Security", "CA5394",
        Justification = "A fixed seed makes the random sequence reproducible; no security decision depends on it.")]
    public void Apply_OverRandomOperationsOnAnAccountWithoutOverdraft_NeverGoesNegative()
    {
        var random = new Random(Seed + 1);
        var balance = AccountBalance.Create(Account, "BRL", 0.00m, 0.00m).Value;

        for (var index = 0; index < Operations; index++)
        {
            var type = random.Next(2) == 0 ? EntryType.Credit : EntryType.Debit;
            var amount = Money.CreatePositive(random.Next(1, 50_001) / 100m, "BRL").Value;

            var result = balance.Apply(type, amount);
            if (result.IsSuccess)
            {
                balance = result.Value;
            }

            balance.Amount.ShouldBeGreaterThanOrEqualTo(0.00m);
        }
    }

    [SuppressMessage("Security", "CA5394",
        Justification = "A fixed seed makes the random sequence reproducible; no security decision depends on it.")]
    private static decimal Run(Random random)
    {
        var balance = AccountBalance.Create(Account, "BRL", 0.00m, Limit).Value;

        for (var index = 0; index < Operations; index++)
        {
            var type = random.Next(2) == 0 ? EntryType.Credit : EntryType.Debit;
            var amount = Money.CreatePositive(random.Next(1, 50_001) / 100m, "BRL").Value;

            var result = balance.Apply(type, amount);
            if (result.IsSuccess)
            {
                balance = result.Value;
            }
        }

        return balance.Amount;
    }
}
