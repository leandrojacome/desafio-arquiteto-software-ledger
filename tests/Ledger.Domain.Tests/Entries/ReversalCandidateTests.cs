using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class ReversalCandidateTests
{
    private static readonly EntryId Original = EntryId.From(Guid.Parse("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10")).Value;
    private static readonly EntryId Other = EntryId.From(Guid.Parse("0192b7c9-3e40-7b5d-8a17-c04d2f6e9b51")).Value;

    [Fact]
    public void Plan_ForADebit_IsACreditOfTheSameAmountAndCurrency()
    {
        var amount = Money.Create(80.00m, "BRL").Value;
        var candidate = new ReversalCandidate(Original, EntryType.Debit, amount, null, null);

        var result = candidate.Plan();

        result.IsSuccess.ShouldBeTrue();
        result.Value.OriginalId.ShouldBe(Original);
        result.Value.Type.ShouldBe(EntryType.Credit);
        result.Value.Amount.ShouldBe(amount);
        result.Value.Amount.Currency.ShouldBe("BRL");
    }

    [Fact]
    public void Plan_ForACredit_IsADebitOfTheSameAmountAndCurrency()
    {
        var amount = Money.Create(1234.56m, "BRL").Value;
        var candidate = new ReversalCandidate(Original, EntryType.Credit, amount, null, null);

        var result = candidate.Plan();

        result.Value.OriginalId.ShouldBe(Original);
        result.Value.Type.ShouldBe(EntryType.Debit);
        result.Value.Amount.ShouldBe(amount);
        result.Value.Amount.Currency.ShouldBe("BRL");
    }

    [Fact]
    public void Plan_ForTheLargestAmount_KeepsTheAmount()
    {
        var amount = Money.CreatePositive(Money.MaxAbsoluteAmount, "BRL").Value;
        var candidate = new ReversalCandidate(Original, EntryType.Credit, amount, null, null);

        candidate.Plan().Value.Amount.ShouldBe(amount);
    }

    [Fact]
    public void Plan_WhenTheEntryIsItselfAReversal_ReturnsNotReversible()
    {
        var amount = Money.Create(80.00m, "BRL").Value;
        var candidate = new ReversalCandidate(Original, EntryType.Credit, amount, Other, null);

        var result = candidate.Plan();

        result.Error.ShouldBe(EntryErrors.NotReversible);
    }

    [Fact]
    public void Plan_WhenTheEntryWasAlreadyReversed_ReturnsAlreadyReversed()
    {
        var amount = Money.Create(80.00m, "BRL").Value;
        var candidate = new ReversalCandidate(Original, EntryType.Debit, amount, null, Other);

        var result = candidate.Plan();

        result.Error.ShouldBe(EntryErrors.AlreadyReversed);
    }

    [Fact]
    public void Plan_WhenTheEntryIsAReversalThatWasAlsoReversed_ReturnsNotReversibleBeforeAlreadyReversed()
    {
        var amount = Money.Create(80.00m, "BRL").Value;
        var candidate = new ReversalCandidate(Original, EntryType.Credit, amount, Other, Other);

        var result = candidate.Plan();

        result.Error.ShouldBe(EntryErrors.NotReversible);
    }

    [Fact]
    public void Plan_ForAReversalOfAReversal_ReturnsNotReversibleEvenWhenNothingReversedIt()
    {
        var amount = Money.Create(80.00m, "BRL").Value;
        var reversalOfTheOriginal = new ReversalCandidate(Other, EntryType.Credit, amount, Original, null);

        reversalOfTheOriginal.Plan().Error.ShouldBe(EntryErrors.NotReversible);
    }

    [Fact]
    public void Plan_ForAnEntryReversedTwice_ReturnsAlreadyReversedEveryTime()
    {
        var amount = Money.Create(80.00m, "BRL").Value;
        var candidate = new ReversalCandidate(Original, EntryType.Debit, amount, null, Other);

        candidate.Plan().Error.ShouldBe(EntryErrors.AlreadyReversed);
        candidate.Plan().Error.ShouldBe(EntryErrors.AlreadyReversed);
    }

    [Fact]
    public void Plan_IsPure_TwoCallsGiveEqualPlans()
    {
        var amount = Money.Create(80.00m, "BRL").Value;
        var candidate = new ReversalCandidate(Original, EntryType.Debit, amount, null, null);

        candidate.Plan().Value.ShouldBe(candidate.Plan().Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(42)]
    public void Plan_ForUnknownEntryType_Throws(int raw)
    {
        var amount = Money.Create(80.00m, "BRL").Value;
        var candidate = new ReversalCandidate(Original, (EntryType)raw, amount, null, null);

        Should.Throw<ArgumentOutOfRangeException>(() => candidate.Plan());
    }

    [Fact]
    public void ToString_OfTheCandidateAndOfItsPlan_NeverPrintsTheAmount()
    {
        var candidate = new ReversalCandidate(Original, EntryType.Debit, Money.Create(1234.56m, "BRL").Value, null, Other);

        var plan = new ReversalCandidate(Original, EntryType.Debit, Money.Create(1234.56m, "BRL").Value, null, null).Plan().Value;

        candidate.ToString().ShouldContain(Original.ToString());
        candidate.ToString().ShouldNotContain("1234");
        plan.ToString().ShouldContain(Original.ToString());
        plan.ToString().ShouldContain("Credit");
        plan.ToString().ShouldNotContain("1234");
    }

    [Fact]
    public void ReversalCandidate_UsesValueEquality()
    {
        var amount = Money.Create(80.00m, "BRL").Value;
        var first = new ReversalCandidate(Original, EntryType.Debit, amount, null, null);
        var second = new ReversalCandidate(Original, EntryType.Debit, Money.Create(80m, "BRL").Value, null, null);

        first.ShouldBe(second);
    }
}
