namespace Ledger.Api.Validation;

internal sealed record RequestBody(byte[] Bytes, bool IsTooLarge)
{
    public static RequestBody TooLarge { get; } = new([], true);

    public bool IsEmpty => Bytes.Length == 0;
}
