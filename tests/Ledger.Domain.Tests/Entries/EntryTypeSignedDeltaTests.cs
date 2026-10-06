using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class EntryTypeSignedDeltaTests
{
    [Fact]
    public void SignedDelta_ForCredit_IsThePositiveAmount()
    {
        var amount = Money.CreatePositive(80.00m, "BRL").Value;

        var delta = EntryType.Credit.SignedDelta(amount);

        delta.ShouldBe(80.00m);
    }

    [Fact]
    public void SignedDelta_ForDebit_IsTheNegativeAmount()
    {
        var amount = Money.CreatePositive(80.00m, "BRL").Value;

        var delta = EntryType.Debit.SignedDelta(amount);

        delta.ShouldBe(-80.00m);
    }

    [Fact]
    public void SignedDelta_ForTheSmallestAmount_KeepsOneCent()
    {
        var amount = Money.CreatePositive(0.01m, "BRL").Value;

        EntryType.Credit.SignedDelta(amount).ShouldBe(0.01m);
        EntryType.Debit.SignedDelta(amount).ShouldBe(-0.01m);
    }

    [Fact]
    public void SignedDelta_ForTheLargestAmount_DoesNotLosePrecision()
    {
        var amount = Money.CreatePositive(Money.MaxAbsoluteAmount, "BRL").Value;

        EntryType.Credit.SignedDelta(amount).ShouldBe(Money.MaxAbsoluteAmount);
        EntryType.Debit.SignedDelta(amount).ShouldBe(-Money.MaxAbsoluteAmount);
    }

    [Fact]
    public void SignedDelta_OfADebitAndOfACreditWithTheSameAmount_CancelEachOther()
    {
        var amount = Money.CreatePositive(123.45m, "BRL").Value;

        var total = EntryType.Credit.SignedDelta(amount) + EntryType.Debit.SignedDelta(amount);

        total.ShouldBe(0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(42)]
    [InlineData(-1)]
    public void SignedDelta_ForUnknownEntryType_Throws(int raw)
    {
        var unknown = (EntryType)raw;
        var amount = Money.CreatePositive(80.00m, "BRL").Value;

        Should.Throw<ArgumentOutOfRangeException>(() => unknown.SignedDelta(amount));
    }
}
