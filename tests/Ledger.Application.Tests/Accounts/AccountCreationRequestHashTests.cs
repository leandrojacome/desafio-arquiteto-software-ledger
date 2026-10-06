using Ledger.Application.Accounts;
using Ledger.Domain.Shared;

namespace Ledger.Application.Tests.Accounts;

[Trait("Category", "Unit")]
public sealed class AccountCreationRequestHashTests
{
    private static readonly byte[] Index = [7, 7, 7, 7];

    private static Money Limit(decimal amount) => Money.Create(amount, "BRL").Value;

    [Fact]
    public void Compute_IsDeterministicAndHas32Bytes()
    {
        var first = AccountCreationRequestHash.Compute("BRL", Limit(10m), Index);
        var second = AccountCreationRequestHash.Compute("BRL", Limit(10m), Index);

        first.Length.ShouldBe(32);
        first.ShouldBe(second);
    }

    [Fact]
    public void Compute_ChangesWhenTheOverdraftLimitChanges()
    {
        AccountCreationRequestHash.Compute("BRL", Limit(10m), Index)
            .ShouldNotBe(AccountCreationRequestHash.Compute("BRL", Limit(10.01m), Index));
    }

    [Fact]
    public void Compute_ChangesWhenTheCurrencyChanges()
    {
        AccountCreationRequestHash.Compute("BRL", Limit(10m), Index)
            .ShouldNotBe(AccountCreationRequestHash.Compute("EUR", Limit(10m), Index));
    }

    [Fact]
    public void Compute_ChangesWhenTheBlindIndexChanges()
    {
        AccountCreationRequestHash.Compute("BRL", Limit(10m), Index)
            .ShouldNotBe(AccountCreationRequestHash.Compute("BRL", Limit(10m), [7, 7, 7, 8]));
    }

    [Fact]
    public void Compute_TreatsEquivalentAmountsAsTheSame()
    {
        AccountCreationRequestHash.Compute("BRL", Limit(10m), Index)
            .ShouldBe(AccountCreationRequestHash.Compute("BRL", Limit(10.00m), Index));
    }

    [Fact]
    public void Matches_IsTrueOnlyForTheSameHash()
    {
        var stored = AccountCreationRequestHash.Compute("BRL", Limit(10m), Index);

        AccountCreationRequestHash.Matches(stored, AccountCreationRequestHash.Compute("BRL", Limit(10m), Index)).ShouldBeTrue();
        AccountCreationRequestHash.Matches(stored, AccountCreationRequestHash.Compute("BRL", Limit(11m), Index)).ShouldBeFalse();
        AccountCreationRequestHash.Matches(stored, stored.AsSpan(0, 16)).ShouldBeFalse();
    }
}
