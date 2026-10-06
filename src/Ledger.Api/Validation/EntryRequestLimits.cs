namespace Ledger.Api.Validation;

internal static class EntryRequestLimits
{
    public const decimal MaxAmount = 999_999_999.99m;

    public const int MaxDescriptionLength = 140;

    public const int MaxReferenceLength = 100;

    public const int MaxDecimalPlaces = 2;

    public const int MaxFractionDigitsOfInstant = 6;

    public const int MaxZeroPaddedFractionDigitsOfInstant = 9;

    public static readonly DateTimeOffset EarliestOccurredAt = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
