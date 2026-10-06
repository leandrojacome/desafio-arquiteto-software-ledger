using Ledger.Api.Validation;

namespace Ledger.Api.IntegrationTests.Reads;

[Trait("Category", "Unit")]
public sealed class InstantParameterReaderTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 1, 14, 3, 11, TimeSpan.Zero);

    public static TheoryData<string, long> AcceptedInstants => new()
    {
        { "2026-10-01T14:03:11Z", Base.UtcTicks },
        { "2026-10-01T14:03:11.482913Z", Base.UtcTicks + 4_829_130 },
        { "2026-10-01T14:03:11+00:00", Base.UtcTicks },
        { "2026-10-01T11:03:11-03:00", Base.UtcTicks },
        { "2026-10-01T11:03:11.482913-03:00", Base.UtcTicks + 4_829_130 },
        { "2026-10-01T19:33:11+05:30", Base.UtcTicks },
        { "2026-10-01T14:03:11.5+00:00", Base.UtcTicks + 5_000_000 },
        { "2026-10-01T14:03:11.000001Z", Base.UtcTicks + 10 },
        { "2026-10-01T14:03:11.000000Z", Base.UtcTicks },
        { "2026-10-01T14:03:11.1Z", Base.UtcTicks + 1_000_000 },
        { "2026-02-28T23:59:59.999999Z", new DateTimeOffset(2026, 2, 28, 23, 59, 59, TimeSpan.Zero).UtcTicks + 9_999_990 }
    };

    public static TheoryData<string?> RejectedInstants =>
    [
        "2026-10-01T14:03:11",
        "2026-10-01T14:03:11.5",
        "2026-10-01T14:03:11.Z",
        "2026-10-01T14:03:11+0000",
        "2026-10-01T14:03:11-00:00",
        "2026-10-01T14:03:11+25:00",
        "2026-10-01T14:03:11-15:00",
        "2026-10-01T14:03:11+14:01",
        "2026-10-01T14:03:11.4829131Z",
        "2026-10-01",
        "2026-10-01T24:00:00Z",
        "2026-02-30T00:00:00Z",
        "2026-13-01T00:00:00Z",
        "2026-10-01T23:60:00Z",
        "2026-10-01T23:59:60Z",
        "0000-01-01T00:00:00Z",
        "٢٠٢٦-10-01T14:03:11Z",
        "2026-10-01T14:03:11Z\n",
        " 2026-10-01T14:03:11Z",
        "2026-10-01T14:03:11Z ",
        "2026-10-01t14:03:11Z",
        "2026-10-01T14:03:11z",
        "2026-10-01 14:03:11Z",
        "2026-10-01T14:03:11,5Z",
        "2026-10-01T14:03:11+00:00Z",
        "",
        "abc",
        null
    ];

    [Theory]
    [MemberData(nameof(AcceptedInstants))]
    public void TryRead_WithAnInstantWithAnOffsetInTheStrictGrammar_ReturnsItInUtcWithExactTicks(string text, long expectedTicks)
    {
        var accepted = InstantParameterReader.TryRead(text, out var instant);

        accepted.ShouldBeTrue();
        instant.UtcTicks.ShouldBe(expectedTicks);
        instant.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [MemberData(nameof(RejectedInstants))]
    public void TryRead_WithAnythingOutsideTheStrictGrammar_Refuses(string? text)
    {
        var accepted = InstantParameterReader.TryRead(text, out var instant);

        accepted.ShouldBeFalse();
        instant.ShouldBe(default);
    }

    [Fact]
    public void TryRead_WithTheZuluTheZeroOffsetAndTheBrasiliaForms_ReturnsTheSameInstant()
    {
        InstantParameterReader.TryRead("2026-10-01T14:03:11.482913Z", out var zulu).ShouldBeTrue();
        InstantParameterReader.TryRead("2026-10-01T14:03:11.482913+00:00", out var offset).ShouldBeTrue();
        InstantParameterReader.TryRead("2026-10-01T11:03:11.482913-03:00", out var brasilia).ShouldBeTrue();

        zulu.ShouldBe(offset);
        zulu.ShouldBe(brasilia);
        brasilia.Offset.ShouldBe(TimeSpan.Zero);
    }
}
