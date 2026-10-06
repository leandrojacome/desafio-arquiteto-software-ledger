using Ledger.Domain.Entries;

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
