namespace Ledger.Infrastructure.Observability.Labels;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
internal sealed class LabelAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}
