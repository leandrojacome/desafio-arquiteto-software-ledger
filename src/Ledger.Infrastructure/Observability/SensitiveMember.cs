using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Ledger.Infrastructure.Observability;

internal sealed class SensitiveMember(PropertyInfo property, bool isSensitive)
{
    public string Name { get; } = property.Name;

    public bool IsSensitive { get; } = isSensitive;

    [SuppressMessage("Design", "CA1031", Justification = "A throwing property getter must never break logging.")]
    public object? Read(object instance)
    {
        try
        {
            return property.GetValue(instance);
        }
        catch (Exception exception)
        {
            var cause = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;

            return $"The property accessor threw {cause.GetType().Name}";
        }
    }
}
