using System.Collections.Frozen;
using System.Reflection;

namespace Ledger.Infrastructure.Observability.Labels;

internal static class LabelTable<TEnum>
    where TEnum : struct, Enum
{
    private static readonly FrozenDictionary<TEnum, string> TextByValue = BuildTextByValue();

    private static readonly FrozenDictionary<string, TEnum> ValueByText =
        TextByValue.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> Texts => ValueByText.Keys;

    public static string Of(TEnum value) => TextByValue[value];

    public static bool TryParse(string? text, out TEnum value)
    {
        if (text is not null && ValueByText.TryGetValue(text, out value))
        {
            return true;
        }

        value = default;

        return false;
    }

    private static FrozenDictionary<TEnum, string> BuildTextByValue()
    {
        var map = new Dictionary<TEnum, string>();

        foreach (var value in Enum.GetValues<TEnum>())
        {
            var name = Enum.GetName(value) ?? throw new InvalidOperationException("Enum member without a name.");
            var field = typeof(TEnum).GetField(name, BindingFlags.Public | BindingFlags.Static);
            var label = field?.GetCustomAttribute<LabelAttribute>()
                        ?? throw new InvalidOperationException($"Member {name} of {typeof(TEnum).Name} has no label.");

            map.Add(value, label.Text);
        }

        return map.ToFrozenDictionary();
    }
}
