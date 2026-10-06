using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class EntryTypeTests
{
    [Fact]
    public void Default_IsNotAnyDefinedEntryType()
    {
        Enum.IsDefined(default(EntryType)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(EntryType.Credit)]
    [InlineData(EntryType.Debit)]
    public void DefinedTypes_AreNeverTheDefaultValue(EntryType type)
    {
        type.ShouldNotBe(default);
    }
}

[Trait("Category", "Unit")]
public sealed class EntryTypeExtensionsTests
{
    [Theory]
    [InlineData(EntryType.Credit, EntryType.Debit)]
    [InlineData(EntryType.Debit, EntryType.Credit)]
    public void Opposite_ReturnsTheInverseType(EntryType type, EntryType expected)
    {
        type.Opposite().ShouldBe(expected);
    }

    [Fact]
    public void Opposite_ForUnknownValue_Throws()
    {
        var unknown = (EntryType)42;

        Should.Throw<ArgumentOutOfRangeException>(() => unknown.Opposite());
    }

    [Fact]
    public void Opposite_ForTheDefaultValue_Throws()
    {
        var unset = default(EntryType);

        Should.Throw<ArgumentOutOfRangeException>(() => unset.Opposite());
    }

    [Theory]
    [InlineData(EntryType.Credit, "CREDIT")]
    [InlineData(EntryType.Debit, "DEBIT")]
    public void ToDatabaseText_ReturnsTheUppercaseText(EntryType type, string expected)
    {
        type.ToDatabaseText().ShouldBe(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(42)]
    public void ToDatabaseText_ForUndefinedValue_Throws(int raw)
    {
        var unknown = (EntryType)raw;

        Should.Throw<ArgumentOutOfRangeException>(() => unknown.ToDatabaseText());
    }
}

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

[Trait("Category", "Unit")]
public sealed class EntryTypeTextTests
{
    [Theory]
    [InlineData("CREDIT", EntryType.Credit)]
    [InlineData("DEBIT", EntryType.Debit)]
    public void TryParse_WithTheExactText_ReturnsTheType(string text, EntryType expected)
    {
        var parsed = EntryTypeText.TryParse(text, out var type);

        parsed.ShouldBeTrue();
        type.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("credit")]
    [InlineData("Debit")]
    [InlineData(" CREDIT")]
    [InlineData("DEBIT ")]
    [InlineData("1")]
    [InlineData("CREDIT,DEBIT")]
    [InlineData("TRANSFER")]
    public void TryParse_WithAnythingElse_ReturnsFalseAndTheDefault(string? text)
    {
        var parsed = EntryTypeText.TryParse(text, out var type);

        parsed.ShouldBeFalse();
        type.ShouldBe(default);
    }

    [Theory]
    [InlineData(EntryType.Credit)]
    [InlineData(EntryType.Debit)]
    public void DatabaseText_RoundTripsEveryDefinedType(EntryType original)
    {
        EntryTypeText.TryParse(original.ToDatabaseText(), out var copy).ShouldBeTrue();

        copy.ShouldBe(original);
    }
}
