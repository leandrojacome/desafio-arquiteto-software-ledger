using Ledger.Application.Outbox;

namespace Ledger.Infrastructure.Observability.Labels;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
internal sealed class LabelAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

internal static class LabelExtensions
{
    public static string Label<TEnum>(this TEnum value)
        where TEnum : struct, Enum => LabelTable<TEnum>.Of(value);

    public static string Label(this WorkerLoop loop) => loop.Name();
}
