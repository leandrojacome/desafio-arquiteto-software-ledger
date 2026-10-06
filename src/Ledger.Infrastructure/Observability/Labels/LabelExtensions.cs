using Ledger.Application.Outbox;

namespace Ledger.Infrastructure.Observability.Labels;

internal static class LabelExtensions
{
    public static string Label<TEnum>(this TEnum value)
        where TEnum : struct, Enum => LabelTable<TEnum>.Of(value);

    public static string Label(this WorkerLoop loop) => loop.Name();
}
