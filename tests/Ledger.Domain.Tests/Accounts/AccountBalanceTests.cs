using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Tests.Accounts;

[Trait("Category", "Unit")]
public sealed class AccountBalanceTests
{
    private static readonly AccountId Account = AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;

    [Fact]
    public void Create_WithValidValues_KeepsEveryField()
    {
        var result = AccountBalance.Create(Account, "BRL", 100.00m, 500.00m);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccountId.ShouldBe(Account);
        result.Value.Currency.ShouldBe("BRL");
        result.Value.Amount.ShouldBe(100.00m);
        result.Value.OverdraftLimit.ShouldBe(500.00m);
    }

    [Fact]
    public void Create_WithABalanceExactlyAtMinusTheLimit_IsAccepted()
    {
        var result = AccountBalance.Create(Account, "BRL", -500.00m, 500.00m);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(-500.00m);
    }

    [Fact]
    public void Create_WithABalanceOneCentBelowMinusTheLimit_ReturnsBalanceBelowOverdraftLimit()
    {
        var result = AccountBalance.Create(Account, "BRL", -500.01m, 500.00m);

        result.Error.ShouldBe(AccountErrors.BalanceBelowOverdraftLimit);
    }

    [Fact]
    public void Create_WithNegativeOverdraftLimit_ReturnsInvalidOverdraftLimit()
    {
        var result = AccountBalance.Create(Account, "BRL", 0m, -0.01m);

        result.Error.ShouldBe(AccountErrors.InvalidOverdraftLimit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("BR")]
    [InlineData("brl")]
    [InlineData("BRLL")]
    public void Create_WithInvalidCurrency_ReturnsInvalidCurrency(string currency)
    {
        var result = AccountBalance.Create(Account, currency, 0m, 0m);

        result.Error.ShouldBe(MoneyErrors.InvalidCurrency);
    }

    [Fact]
    public void Create_WithMoreThanTwoDecimalPlacesInTheBalance_ReturnsTooManyDecimals()
    {
        var result = AccountBalance.Create(Account, "BRL", 10.005m, 0m);

        result.Error.ShouldBe(MoneyErrors.TooManyDecimals);
    }

    [Fact]
    public void Create_WithMoreThanTwoDecimalPlacesInTheLimit_ReturnsTooManyDecimals()
    {
        var result = AccountBalance.Create(Account, "BRL", 0m, 0.001m);

        result.Error.ShouldBe(MoneyErrors.TooManyDecimals);
    }

    [Fact]
    public void Create_WithABalanceAboveTheColumnCapacity_ReturnsOutOfRange()
    {
        var result = AccountBalance.Create(Account, "BRL", Money.MaxAbsoluteAmount + 0.01m, 0m);

        result.Error.ShouldBe(MoneyErrors.OutOfRange);
    }

    [Fact]
    public void Create_WithALimitAboveTheColumnCapacity_ReturnsOutOfRange()
    {
        var result = AccountBalance.Create(Account, "BRL", 0m, Money.MaxAbsoluteAmount + 0.01m);

        result.Error.ShouldBe(MoneyErrors.OutOfRange);
    }

    [Fact]
    public void Apply_DebitOfExactlyTheBalanceWithZeroLimit_IsAcceptedAndLeavesZero()
    {
        var balance = Balance(100.00m, 0.00m);

        var result = balance.Apply(EntryType.Debit, Brl(100.00m));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(0.00m);
    }

    [Fact]
    public void Apply_DebitOneCentAboveTheBalanceWithZeroLimit_ReturnsInsufficientFunds()
    {
        var balance = Balance(100.00m, 0.00m);

        var result = balance.Apply(EntryType.Debit, Brl(100.01m));

        result.Error.ShouldBe(EntryErrors.InsufficientFunds);
    }

    [Fact]
    public void Apply_DebitThatLeavesExactlyMinusTheLimit_IsAcceptedWithTheSameOperatorAsTheDatabase()
    {
        var balance = Balance(100.00m, 500.00m);

        var result = balance.Apply(EntryType.Debit, Brl(600.00m));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(-500.00m);
    }

    [Fact]
    public void Apply_DebitThatLeavesOneCentBelowMinusTheLimit_ReturnsInsufficientFunds()
    {
        var balance = Balance(100.00m, 500.00m);

        var result = balance.Apply(EntryType.Debit, Brl(600.01m));

        result.Error.ShouldBe(EntryErrors.InsufficientFunds);
    }

    [Fact]
    public void Apply_AnyDebitOnAnEmptyAccountWithZeroLimit_ReturnsInsufficientFunds()
    {
        var balance = Balance(0.00m, 0.00m);

        var result = balance.Apply(EntryType.Debit, Brl(0.01m));

        result.Error.ShouldBe(EntryErrors.InsufficientFunds);
    }

    [Fact]
    public void Apply_DebitOnAnAccountAlreadyAtTheFloor_ReturnsInsufficientFunds()
    {
        var balance = Balance(-500.00m, 500.00m);

        var result = balance.Apply(EntryType.Debit, Brl(0.01m));

        result.Error.ShouldBe(EntryErrors.InsufficientFunds);
    }

    [Theory]
    [InlineData("0.00", "0.00")]
    [InlineData("100.00", "0.00")]
    [InlineData("100.00", "500.00")]
    [InlineData("-500.00", "500.00")]
    [InlineData("-0.01", "0.01")]
    public void Apply_Credit_IsAlwaysAccepted(string balanceText, string limitText)
    {
        var balance = Balance(Parse(balanceText), Parse(limitText));

        var result = balance.Apply(EntryType.Credit, Brl(0.01m));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(Parse(balanceText) + 0.01m);
    }

    [Fact]
    public void Apply_CreditOnAnAccountAtTheFloor_BringsItBackAboveTheFloor()
    {
        var balance = Balance(-500.00m, 500.00m);

        var result = balance.Apply(EntryType.Credit, Brl(250.00m));

        result.Value.Amount.ShouldBe(-250.00m);
    }

    [Theory]
    [InlineData(EntryType.Credit)]
    [InlineData(EntryType.Debit)]
    public void Apply_WithADifferentCurrency_ReturnsCurrencyMismatch(EntryType type)
    {
        var balance = Balance(100.00m, 0.00m);
        var otherCurrency = Money.Create(10.00m, "EUR").Value;

        var result = balance.Apply(type, otherCurrency);

        result.Error.ShouldBe(AccountErrors.CurrencyMismatch);
    }

    [Fact]
    public void Apply_WithADifferentCurrencyAndAnAmountThatDoesNotFit_ReportsTheCurrencyFirst()
    {
        var balance = Balance(100.00m, 0.00m);
        var otherCurrency = Money.Create(1000.00m, "EUR").Value;

        var result = balance.Apply(EntryType.Debit, otherCurrency);

        result.Error.ShouldBe(AccountErrors.CurrencyMismatch);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10.00")]
    public void Apply_WithZeroOrNegativeMoney_ReturnsMustBePositiveInsteadOfTurningADebitIntoACredit(string text)
    {
        var balance = Balance(100.00m, 0.00m);
        var amount = Money.Create(Parse(text), "BRL").Value;

        balance.Apply(EntryType.Debit, amount).Error.ShouldBe(MoneyErrors.MustBePositive);
        balance.Apply(EntryType.Credit, amount).Error.ShouldBe(MoneyErrors.MustBePositive);
    }

    [Fact]
    public void Apply_CreditThatReachesExactlyTheColumnCapacity_IsAccepted()
    {
        var balance = Balance(Money.MaxAbsoluteAmount - 0.01m, 0.00m);

        var result = balance.Apply(EntryType.Credit, Brl(0.01m));

        result.Value.Amount.ShouldBe(Money.MaxAbsoluteAmount);
    }

    [Fact]
    public void Apply_CreditThatPassesTheColumnCapacity_ReturnsOutOfRange()
    {
        var balance = Balance(Money.MaxAbsoluteAmount, 0.00m);

        var result = balance.Apply(EntryType.Credit, Brl(0.01m));

        result.Error.ShouldBe(MoneyErrors.OutOfRange);
    }

    [Fact]
    public void Apply_DoesNotChangeTheOriginalBalance()
    {
        var balance = Balance(100.00m, 0.00m);

        var applied = balance.Apply(EntryType.Debit, Brl(40.00m));

        applied.Value.Amount.ShouldBe(60.00m);
        balance.Amount.ShouldBe(100.00m);
    }

    [Fact]
    public void Apply_KeepsTheAccountCurrencyAndLimit()
    {
        var balance = Balance(100.00m, 500.00m);

        var applied = balance.Apply(EntryType.Debit, Brl(40.00m)).Value;

        applied.AccountId.ShouldBe(Account);
        applied.Currency.ShouldBe("BRL");
        applied.OverdraftLimit.ShouldBe(500.00m);
    }

    [Fact]
    public void Apply_ChainedDebitAndItsReversalCredit_ReturnsTheOriginalBalance()
    {
        var balance = Balance(100.00m, 500.00m);

        var debited = balance.Apply(EntryType.Debit, Brl(333.33m)).Value;
        var restored = debited.Apply(EntryType.Credit, Brl(333.33m)).Value;

        restored.ShouldBe(balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(42)]
    public void Apply_WithUnknownEntryType_Throws(int raw)
    {
        var balance = Balance(100.00m, 0.00m);

        Should.Throw<ArgumentOutOfRangeException>(() => balance.Apply((EntryType)raw, Brl(1.00m)));
    }

    [Fact]
    public void Equals_ForTheSameValues_IsTrue()
    {
        var first = Balance(100.00m, 500.00m);
        var second = Balance(100m, 500m);

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Equals_ForDifferentBalances_IsFalse()
    {
        Balance(100.00m, 500.00m).ShouldNotBe(Balance(100.01m, 500.00m));
    }

    [Fact]
    public void ToString_NeverPrintsTheBalanceNorTheLimit()
    {
        var balance = Balance(1234.56m, 789.01m);

        var printed = balance.ToString();

        printed.ShouldContain(Account.ToString());
        printed.ShouldContain("BRL");
        printed.ShouldNotContain("1234");
        printed.ShouldNotContain("789");
    }

    private static AccountBalance Balance(decimal amount, decimal overdraftLimit) =>
        AccountBalance.Create(Account, "BRL", amount, overdraftLimit).Value;

    private static Money Brl(decimal amount) => Money.Create(amount, "BRL").Value;

    private static decimal Parse(string text) =>
        decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
}
