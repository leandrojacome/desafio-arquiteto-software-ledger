using Ledger.Domain.Entries;

namespace Ledger.Domain.Tests.Entries;

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
