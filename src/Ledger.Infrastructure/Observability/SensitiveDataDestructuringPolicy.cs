using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Infrastructure.Observability;

internal sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    private static readonly ConcurrentDictionary<Type, SensitiveTypeShape?> Shapes = new();

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        var type = value.GetType();
        var shape = Shapes.GetOrAdd(type, SensitiveTypeShape.Create);

        if (shape is null)
        {
            result = null;

            return false;
        }

        var properties = new List<LogEventProperty>(shape.Members.Count);

        foreach (var member in shape.Members)
        {
            LogEventPropertyValue propertyValue = member.IsSensitive
                ? new ScalarValue(SensitiveMemberNames.Mask)
                : propertyValueFactory.CreatePropertyValue(member.Read(value), true);

            properties.Add(new LogEventProperty(member.Name, propertyValue));
        }

        result = new StructureValue(properties, type.Name);

        return true;
    }
}
