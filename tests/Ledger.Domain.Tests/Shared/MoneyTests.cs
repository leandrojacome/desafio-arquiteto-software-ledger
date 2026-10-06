using System.Globalization;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Tests.Shared;

[Trait("Category", "Unit")]
public sealed class MoneyTests
{
    [Fact]
    public void Create_WithValidAmountAndCurrency_ReturnsMoney()
    {
        var result = Money.Create(80m, "BRL");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(80m);
        result.Value.Currency.ShouldBe("BRL");
    }

    [Theory]
    [InlineData("80", "80.00")]
    [InlineData("80.5", "80.50")]
    [InlineData("0.07", "0.07")]
    [InlineData("1234567.89", "1234567.89")]
    [InlineData("-15", "-15.00")]
    [InlineData("-0.01", "-0.01")]
    public void ToDecimalString_AlwaysUsesTwoDecimalPlaces(string amount, string expected)
    {
        var money = Money.Create(Parse(amount), "BRL").Value;

        money.ToDecimalString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("BR")]
    [InlineData("BRLL")]
    [InlineData("brl")]
    [InlineData("B1L")]
    public void Create_WithInvalidCurrency_ReturnsInvalidCurrency(string currency)
    {
        var result = Money.Create(10m, currency);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(MoneyErrors.InvalidCurrency);
    }

    [Fact]
    public void Create_WithMoreThanTwoDecimalPlaces_ReturnsTooManyDecimalsInsteadOfRounding()
    {
        var result = Money.Create(10.005m, "BRL");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(MoneyErrors.TooManyDecimals);
    }

    [Fact]
    public void Create_WithTrailingZerosBeyondTwoPlaces_IsAccepted()
    {
        var result = Money.Create(10.500m, "BRL");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ToDecimalString().ShouldBe("10.50");
    }

    [Fact]
    public void Create_WithNegativeAmount_KeepsTheSign()
    {
        var result = Money.Create(-15.5m, "BRL");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(-15.5m);
        result.Value.IsPositive.ShouldBeFalse();
    }

    [Fact]
    public void Create_WithExactlyTheMaximumAmount_IsAccepted()
    {
        var result = Money.Create(Money.MaxAbsoluteAmount, "BRL");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(Money.MaxAbsoluteAmount);
    }

    [Fact]
    public void Create_WithExactlyTheNegativeMaximumAmount_IsAccepted()
    {
        var result = Money.Create(-Money.MaxAbsoluteAmount, "BRL");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(-Money.MaxAbsoluteAmount);
    }

    [Fact]
    public void Create_OneCentAboveTheMaximumAmount_ReturnsOutOfRange()
    {
        var result = Money.Create(Money.MaxAbsoluteAmount + 0.01m, "BRL");

        result.Error.ShouldBe(MoneyErrors.OutOfRange);
    }

    [Fact]
    public void Create_OneCentBelowTheNegativeMaximumAmount_ReturnsOutOfRange()
    {
        var result = Money.Create(-Money.MaxAbsoluteAmount - 0.01m, "BRL");

        result.Error.ShouldBe(MoneyErrors.OutOfRange);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-0.01")]
    public void CreatePositive_WithZeroOrNegativeAmount_ReturnsMustBePositive(string amount)
    {
        var result = Money.CreatePositive(Parse(amount), "BRL");

        result.Error.ShouldBe(MoneyErrors.MustBePositive);
    }

    [Fact]
    public void CreatePositive_WithPositiveAmount_ReturnsMoney()
    {
        var result = Money.CreatePositive(0.01m, "BRL");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsPositive.ShouldBeTrue();
    }

    [Fact]
    public void CreatePositive_WithExactlyTheMaximumAmount_IsAccepted()
    {
        var result = Money.CreatePositive(Money.MaxAbsoluteAmount, "BRL");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(Money.MaxAbsoluteAmount);
    }

    [Fact]
    public void CreatePositive_OneCentAboveTheMaximumAmount_ReturnsOutOfRange()
    {
        var result = Money.CreatePositive(Money.MaxAbsoluteAmount + 0.01m, "BRL");

        result.Error.ShouldBe(MoneyErrors.OutOfRange);
    }

    [Fact]
    public void CreatePositive_WithInvalidCurrency_ReturnsTheCreationFailure()
    {
        var result = Money.CreatePositive(10m, "br");

        result.Error.ShouldBe(MoneyErrors.InvalidCurrency);
    }

    [Fact]
    public void Equals_WithSameValueAndDifferentScale_IsTrue()
    {
        var first = Money.Create(80m, "BRL").Value;
        var second = Money.Create(80.00m, "BRL").Value;

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Equals_WithDifferentCurrencies_IsFalse()
    {
        var reais = Money.Create(10m, "BRL").Value;
        var otherCurrency = Money.Create(10m, "EUR").Value;

        reais.ShouldNotBe(otherCurrency);
    }

    [Fact]
    public void Create_WithNullCurrency_ReturnsInvalidCurrency()
    {
        var result = Money.Create(10m, null!);

        result.Error.ShouldBe(MoneyErrors.InvalidCurrency);
    }

    [Theory]
    [InlineData("B R")]
    [InlineData("BR1")]
    [InlineData("bRL")]
    [InlineData("ÉUR")]
    [InlineData("BRL ")]
    [InlineData(" BRL")]
    [InlineData("ＢＲＬ")]
    public void Create_WithCurrencyThatIsNotThreeAsciiUppercaseLetters_ReturnsInvalidCurrency(string currency)
    {
        var result = Money.Create(10m, currency);

        result.Error.ShouldBe(MoneyErrors.InvalidCurrency);
    }

    [Fact]
    public void Create_WithZero_IsAcceptedButIsNotPositive()
    {
        var result = Money.Create(0m, "BRL");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsPositive.ShouldBeFalse();
    }

    [Fact]
    public void Create_WithTheSmallestUnit_IsAcceptedAndPositive()
    {
        var result = Money.CreatePositive(0.01m, "BRL");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ToDecimalString().ShouldBe("0.01");
    }

    [Fact]
    public void Create_WithTheExtremesOfTheDecimalType_ReturnsOutOfRange()
    {
        Money.Create(decimal.MaxValue, "BRL").Error.ShouldBe(MoneyErrors.OutOfRange);
        Money.Create(decimal.MinValue, "BRL").Error.ShouldBe(MoneyErrors.OutOfRange);
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("0.0000000000000000000000000001")]
    [InlineData("1.005")]
    [InlineData("-1.005")]
    public void Create_WithMoreThanTwoDecimalPlaces_ReturnsTooManyDecimalsForPositiveAndNegativeValues(string amount)
    {
        var result = Money.Create(Parse(amount), "BRL");

        result.Error.ShouldBe(MoneyErrors.TooManyDecimals);
    }

    [Theory]
    [InlineData("1.10", "1.10")]
    [InlineData("1.1000", "1.10")]
    [InlineData("1.0000000000", "1.00")]
    public void Create_WithTrailingZerosInTheScale_KeepsTwoDecimalPlaces(string amount, string expected)
    {
        var result = Money.Create(Parse(amount), "BRL");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ToDecimalString().ShouldBe(expected);
    }

    [Fact]
    public void ToString_NeverPrintsTheAmount()
    {
        var money = Money.Create(80.5m, "BRL").Value;

        var printed = money.ToString();

        printed.ShouldContain("BRL");
        printed.ShouldNotContain("80");
        $"{money}".ShouldNotContain("80");
    }

    [Fact]
    public void ToDecimalString_AtTheMaximumAmount_KeepsEveryDigit()
    {
        var money = Money.Create(Money.MaxAbsoluteAmount, "BRL").Value;

        money.ToDecimalString().ShouldBe("9999999999999999.99");
    }

    private static decimal Parse(string amount) => decimal.Parse(amount, CultureInfo.InvariantCulture);
}
