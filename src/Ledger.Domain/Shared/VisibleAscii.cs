namespace Ledger.Domain.Shared;

internal static class VisibleAscii
{
    private const char First = '!';
    private const char Last = '~';

    public static bool IsVisible(char character) => character is >= First and <= Last;

    public static bool ContainsOnlyVisible(string text) => text.All(IsVisible);
}
