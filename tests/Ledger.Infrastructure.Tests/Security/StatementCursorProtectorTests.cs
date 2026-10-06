using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Infrastructure.Security;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class StatementCursorProtectorTests
{
    private const string KeyBase64 = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string OtherKeyBase64 = "AgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhscHR4fICE=";
    private const string AccountText = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33";
    private const string V1 = "AQAGXMfgSmQhAAAAAAAABzPzGOCjl5YS7PXaNwBbM93l";
    private const string V2 = "AQAGXMgfF5b8AAAAAAAABzvZbkX-8qxg3DhgJH4ylC6Y";
    private const string V3 = "AQAGXMfgSmQhAAAAAAAABzOF7CDaVVMJMIpQZvl0weAS";
    private const string V4 = "AQAGXMfgSmQhAAAAAAAABzM9vzZISHD62Eplg0dm7r1C";

    private static readonly StatementPosition PositionOne = new(Instant(2026, 10, 1, 14, 3, 11, 482_913), 1843);
    private static readonly StatementPosition PositionTwo = new(Instant(2026, 10, 1, 14, 20, 45, 118_204), 1851);

    private static AccountId Account => AccountId.From(Guid.Parse(AccountText)).Value;

    private static HmacStatementCursorProtector Protector(string keyBase64 = KeyBase64) =>
        HmacStatementCursorProtector.Create(Convert.FromBase64String(keyBase64));

    private static DateTimeOffset Instant(int year, int month, int day, int hour, int minute, int second, int microseconds) =>
        new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero).AddTicks(microseconds * 10L);

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] FromBase64Url(string text) =>
        Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/'));

    private static string Forge(string keyBase64, long microseconds, long position)
    {
        var cursor = new byte[33];
        cursor[0] = 1;
        BinaryPrimitives.WriteInt64BigEndian(cursor.AsSpan(1, 8), microseconds);
        BinaryPrimitives.WriteInt64BigEndian(cursor.AsSpan(9, 8), position);

        var input = new byte[16 + 17];
        Account.Value.TryWriteBytes(input, bigEndian: true, out _);
        cursor.AsSpan(0, 17).CopyTo(input.AsSpan(16));
        HMACSHA256.HashData(Convert.FromBase64String(keyBase64), input).AsSpan(0, 16).CopyTo(cursor.AsSpan(17));

        return ToBase64Url(cursor);
    }

    [Fact]
    public void Protect_FirstVector_ReproducesTheContractCursor()
    {
        Protector().Protect(Account, PositionOne).ShouldBe(V1);
    }

    [Fact]
    public void Protect_SecondVector_ReproducesTheContractCursor()
    {
        Protector().Protect(Account, PositionTwo).ShouldBe(V2);
    }

    [Fact]
    public void Unprotect_FirstVector_ReturnsThePosition()
    {
        var result = Protector().Unprotect(Account, V1);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(PositionOne);
    }

    [Fact]
    public void Unprotect_SecondVector_ReturnsThePosition()
    {
        var result = Protector().Unprotect(Account, V2);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(PositionTwo);
    }

    [Fact]
    public void Unprotect_CursorSignedForAnotherAccount_IsRefused()
    {
        var result = Protector().Unprotect(Account, V3);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(StatementErrors.InvalidCursor);
    }

    [Fact]
    public void Unprotect_CursorSignedWithAnotherKey_IsRefused()
    {
        var result = Protector().Unprotect(Account, V4);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(StatementErrors.InvalidCursor);
    }

    [Fact]
    public void Unprotect_SameCursorWithTheOtherKey_IsAcceptedOnlyWhenTheKeyMatches()
    {
        Protector(OtherKeyBase64).Unprotect(Account, V4).IsSuccess.ShouldBeTrue();
        Protector(OtherKeyBase64).Unprotect(Account, V1).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Unprotect_LastCharacterChanged_IsRefused()
    {
        var changed = V1[..^1] + "m";

        Protector().Unprotect(Account, changed).Error.ShouldBe(StatementErrors.InvalidCursor);
    }

    [Theory]
    [InlineData(V1 + "=")]
    [InlineData("AQAGXMfgSmQhAAAAAAAABzPzGOCjl5YS7PXaNwBbM93")]
    [InlineData(V1 + "A")]
    public void Unprotect_WrongLength_IsRefused(string cursor)
    {
        Protector().Unprotect(Account, cursor).Error.ShouldBe(StatementErrors.InvalidCursor);
    }

    [Fact]
    public void Unprotect_FirstCharacterChangedFromAToB_IsRefused()
    {
        var changed = "B" + V1[1..];

        Protector().Unprotect(Account, changed).Error.ShouldBe(StatementErrors.InvalidCursor);
    }

    [Fact]
    public void Unprotect_StandardBase64Alphabet_IsRefused()
    {
        var standard = V2.Replace('-', '+');

        Protector().Unprotect(Account, standard).Error.ShouldBe(StatementErrors.InvalidCursor);
        Protector().Unprotect(Account, V1.Replace('l', '/')).Error.ShouldBe(StatementErrors.InvalidCursor);
    }

    [Fact]
    public void Unprotect_EmptyOrOversizedText_IsRefused()
    {
        Protector().Unprotect(Account, string.Empty).Error.ShouldBe(StatementErrors.InvalidCursor);
        Protector().Unprotect(Account, new string('A', 65)).Error.ShouldBe(StatementErrors.InvalidCursor);
        Protector().Unprotect(Account, new string('A', 5000)).Error.ShouldBe(StatementErrors.InvalidCursor);
    }

    [Fact]
    public void Unprotect_FlippingEachBitOfTheThirtyThreeBytes_IsAlwaysRefused()
    {
        var original = FromBase64Url(V1);
        var protector = Protector();

        original.Length.ShouldBe(33);

        for (var index = 0; index < original.Length; index++)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                var mutated = (byte[])original.Clone();
                mutated[index] ^= (byte)(1 << bit);

                protector.Unprotect(Account, ToBase64Url(mutated))
                    .IsFailure.ShouldBeTrue($"bit {bit} of byte {index} must be detected");
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394",
        Justification = "The fixed seed reproduces the same sequence on every run.")]
    public void ProtectThenUnprotect_ForOneThousandRandomPositions_ReturnsTheSamePosition()
    {
        var random = new Random(20261001);
        var protector = Protector();
        var firstMicrosecond = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks / 10;
        var lastMicrosecond = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks / 10;

        for (var count = 0; count < 1000; count++)
        {
            var microseconds = random.NextInt64(firstMicrosecond, lastMicrosecond);
            var instant = new DateTimeOffset(microseconds * 10, TimeSpan.Zero);
            var position = new StatementPosition(instant, random.NextInt64(1, 1L << 40));

            var result = protector.Unprotect(Account, protector.Protect(Account, position));

            result.IsSuccess.ShouldBeTrue();
            result.Value.ShouldBe(position);
        }
    }

    [Fact]
    public void Protect_Output_IsFortyFourCharactersOfTheUrlAlphabetWithoutPadding()
    {
        var cursor = Protector().Protect(Account, PositionOne);

        cursor.Length.ShouldBe(44);
        cursor.ShouldNotContain('=');
        cursor.ShouldNotContain('+');
        cursor.ShouldNotContain('/');
    }

    [Fact]
    public void Unprotect_ValidlySignedCursorWithPositionZero_IsRefused()
    {
        var instant = PositionOne.RecordedAt;
        var microseconds = (instant.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10;

        Protector().Unprotect(Account, Forge(KeyBase64, microseconds, 0)).IsFailure.ShouldBeTrue();
        Protector().Unprotect(Account, Forge(KeyBase64, microseconds, -5)).IsFailure.ShouldBeTrue();
        Protector().Unprotect(Account, Forge(KeyBase64, microseconds, 7)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Unprotect_ValidlySignedCursorWithAnInstantOutsideTheRepresentableRange_IsRefused()
    {
        Protector().Unprotect(Account, Forge(KeyBase64, long.MaxValue, 7)).IsFailure.ShouldBeTrue();
        Protector().Unprotect(Account, Forge(KeyBase64, long.MinValue, 7)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Protect_PositionNotGreaterThanZero_Throws()
    {
        var protector = Protector();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            protector.Protect(Account, new StatementPosition(PositionOne.RecordedAt, 0)));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            protector.Protect(Account, StatementPosition.Before(PositionOne.RecordedAt)));
    }

    [Fact]
    public void Protect_InstantBelowTheMicrosecond_Throws()
    {
        var protector = Protector();
        var position = new StatementPosition(PositionOne.RecordedAt.AddTicks(1), 5);

        Should.Throw<ArgumentException>(() => protector.Protect(Account, position));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void Create_KeyOfTheWrongSize_IsRefused(int size)
    {
        Should.Throw<ArgumentException>(() => HmacStatementCursorProtector.Create(new byte[size]));
    }

    [Fact]
    public void Create_KeyOfThirtyTwoBytes_IsAccepted()
    {
        Should.NotThrow(() => HmacStatementCursorProtector.Create(new byte[32]));
    }

    [Fact]
    public void Unprotect_DoesNotRevealWhichStepFailed()
    {
        var protector = Protector();

        var errors = new[]
            {
                protector.Unprotect(Account, V3).Error,
                protector.Unprotect(Account, V4).Error,
                protector.Unprotect(Account, string.Empty).Error,
                protector.Unprotect(Account, V1 + "A").Error
            }
            .Distinct()
            .ToList();

        errors.Count.ShouldBe(1);
        errors[0].Message.ShouldNotContain(V1);
    }
}
