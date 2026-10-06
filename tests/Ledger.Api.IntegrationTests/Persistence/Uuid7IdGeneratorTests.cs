using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Persistence;

[Trait("Category", "Unit")]
public sealed class Uuid7IdGeneratorTests
{
    private const int GeneratedCount = 100_000;
    private const int TimestampBytes = 6;
    private const int VersionByte = 6;
    private const int VariantByte = 8;

    private static readonly DateTimeOffset Instant = DateTimeOffset.Parse(
        "2026-10-01T14:03:11.482Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void NewId_EncodesTheClockInTheFirstFortyEightBits()
    {
        var generator = new Uuid7IdGenerator(new FakeTimeProvider(Instant));

        var bytes = generator.NewId().ToByteArray(bigEndian: true);

        var milliseconds = 0L;

        for (var index = 0; index < TimestampBytes; index++)
        {
            milliseconds = (milliseconds << 8) | bytes[index];
        }

        milliseconds.ShouldBe(Instant.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void NewId_SetsVersionSevenAndTheRfcVariant()
    {
        var generator = new Uuid7IdGenerator(new FakeTimeProvider(Instant));

        var bytes = generator.NewId().ToByteArray(bigEndian: true);

        (bytes[VersionByte] >> 4).ShouldBe(7);
        (bytes[VariantByte] >> 6).ShouldBe(0b10);
    }

    [Fact]
    public void NewId_HundredThousandIds_AreAllDistinct()
    {
        var generator = new Uuid7IdGenerator(new FakeTimeProvider(Instant));

        var ids = Enumerable.Range(0, GeneratedCount).Select(_ => generator.NewId()).ToHashSet();

        ids.Count.ShouldBe(GeneratedCount);
    }

    [Fact]
    public void NewId_WhenTheClockMovesForward_SortsAfterByItsTextForm()
    {
        var time = new FakeTimeProvider(Instant);
        var generator = new Uuid7IdGenerator(time);
        var earlier = generator.NewId();

        time.Advance(TimeSpan.FromMilliseconds(5));
        var later = generator.NewId();

        string.CompareOrdinal(later.ToString(), earlier.ToString()).ShouldBeGreaterThan(0);
    }
}
