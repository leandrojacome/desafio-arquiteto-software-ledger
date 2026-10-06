using System.Globalization;
using Ledger.Application.Entries;
using Ledger.Application.Tests.Entries.Support;

namespace Ledger.Application.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class CanonicalRequestHashInstantTests
{
    private static readonly DateTimeOffset Utc = new(2026, 10, 1, 14, 3, 10, TimeSpan.Zero);

    public static TheoryData<int> OffsetsInMinutes =>
    [
        -840,
        -300,
        -240,
        -180,
        -120,
        -1,
        0,
        1,
        180,
        330,
        345,
        840
    ];

    [Theory]
    [MemberData(nameof(OffsetsInMinutes))]
    public void ForRegistration_TheSameInstantInAnyOffset_ProducesTheSameHash(int offsetMinutes)
    {
        var local = Utc.ToOffset(TimeSpan.FromMinutes(offsetMinutes));

        var inUtc = CanonicalRequestHash.ForRegistration(EntryFixtures.RegisterCommand(occurredAt: Utc));
        var inOffset = CanonicalRequestHash.ForRegistration(EntryFixtures.RegisterCommand(occurredAt: local));

        local.Offset.ShouldBe(TimeSpan.FromMinutes(offsetMinutes));
        inOffset.ShouldBe(inUtc);
    }

    [Fact]
    public void ForRegistration_ZuluAgainstTheZeroOffset_ProducesTheSameHash()
    {
        var zulu = new DateTimeOffset(2026, 10, 1, 14, 3, 10, TimeSpan.Zero);
        var zeroOffset = DateTimeOffset.Parse("2026-10-01T14:03:10+00:00", CultureInfo.InvariantCulture);

        var first = CanonicalRequestHash.ForRegistration(EntryFixtures.RegisterCommand(occurredAt: zulu));
        var second = CanonicalRequestHash.ForRegistration(EntryFixtures.RegisterCommand(occurredAt: zeroOffset));

        second.ShouldBe(first);
    }

    [Fact]
    public void ForRegistration_BrasiliaAgainstTheIndianOffset_ProducesTheSameHash()
    {
        var brasilia = new DateTimeOffset(2026, 10, 1, 11, 3, 10, TimeSpan.FromHours(-3));
        var india = new DateTimeOffset(2026, 10, 1, 19, 33, 10, TimeSpan.FromMinutes(330));

        var first = CanonicalRequestHash.ForRegistration(EntryFixtures.RegisterCommand(occurredAt: brasilia));
        var second = CanonicalRequestHash.ForRegistration(EntryFixtures.RegisterCommand(occurredAt: india));

        second.ShouldBe(first);
    }

    [Fact]
    public void ForRegistration_TheSameWallClockInAnotherOffset_ProducesADifferentHash()
    {
        var brasilia = new DateTimeOffset(2026, 10, 1, 14, 3, 10, TimeSpan.FromHours(-3));

        var inUtc = CanonicalRequestHash.ForRegistration(EntryFixtures.RegisterCommand(occurredAt: Utc));
        var inBrasilia = CanonicalRequestHash.ForRegistration(EntryFixtures.RegisterCommand(occurredAt: brasilia));

        inBrasilia.ShouldNotBe(inUtc);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(1_000_000)]
    public void ForRegistration_AnInstantAtLeastOneMicrosecondApartInAnyOffset_ProducesADifferentHash(long ticks)
    {
        var brasilia = Utc.ToOffset(TimeSpan.FromHours(-3));

        var baseline = CanonicalRequestHash.ForRegistration(EntryFixtures.RegisterCommand(occurredAt: brasilia));
        var later = CanonicalRequestHash.ForRegistration(
            EntryFixtures.RegisterCommand(occurredAt: brasilia.AddTicks(ticks)));

        later.ShouldNotBe(baseline);
    }

    [Fact]
    public void ForRegistration_AHalfSecondWrittenInTwoOffsets_ProducesTheSameHash()
    {
        var inBrasilia = new DateTimeOffset(2026, 10, 1, 11, 3, 10, TimeSpan.FromHours(-3)).AddTicks(5_000_000);
        var inUtc = new DateTimeOffset(2026, 10, 1, 14, 3, 10, TimeSpan.Zero).AddTicks(5_000_000);

        var first = CanonicalRequestHash.ForRegistration(EntryFixtures.RegisterCommand(occurredAt: inBrasilia));
        var second = CanonicalRequestHash.ForRegistration(EntryFixtures.RegisterCommand(occurredAt: inUtc));

        second.ShouldBe(first);
    }
}
