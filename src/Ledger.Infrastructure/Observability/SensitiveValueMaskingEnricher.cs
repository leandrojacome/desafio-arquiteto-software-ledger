using Serilog.Core;
using Serilog.Events;

namespace Ledger.Infrastructure.Observability;

internal sealed class SensitiveValueMaskingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        List<LogEventProperty>? replacements = null;

        foreach (var property in logEvent.Properties)
        {
            var masked = Mask(property.Value);

            if (!ReferenceEquals(masked, property.Value))
            {
                replacements ??= [];
                replacements.Add(new LogEventProperty(property.Key, masked));
            }
        }

        if (replacements is null)
        {
            return;
        }

        foreach (var replacement in replacements)
        {
            logEvent.AddOrUpdateProperty(replacement);
        }
    }

    private static LogEventPropertyValue Mask(LogEventPropertyValue value)
    {
        return value switch
        {
            ScalarValue { Value: string text } scalar => MaskScalar(scalar, text),
            StructureValue structure => MaskStructure(structure),
            SequenceValue sequence => MaskSequence(sequence),
            DictionaryValue dictionary => MaskDictionary(dictionary),
            _ => value
        };
    }

    private static ScalarValue MaskScalar(ScalarValue scalar, string text)
    {
        var masked = SensitiveTextMasker.Apply(text);

        return masked == text ? scalar : new ScalarValue(masked);
    }

    private static StructureValue MaskStructure(StructureValue structure)
    {
        var changed = false;
        var properties = new List<LogEventProperty>(structure.Properties.Count);

        foreach (var property in structure.Properties)
        {
            var masked = Mask(property.Value);
            changed |= !ReferenceEquals(masked, property.Value);
            properties.Add(new LogEventProperty(property.Name, masked));
        }

        return changed ? new StructureValue(properties, structure.TypeTag) : structure;
    }

    private static SequenceValue MaskSequence(SequenceValue sequence)
    {
        var changed = false;
        var elements = new List<LogEventPropertyValue>(sequence.Elements.Count);

        foreach (var element in sequence.Elements)
        {
            var masked = Mask(element);
            changed |= !ReferenceEquals(masked, element);
            elements.Add(masked);
        }

        return changed ? new SequenceValue(elements) : sequence;
    }

    private static DictionaryValue MaskDictionary(DictionaryValue dictionary)
    {
        var changed = false;
        var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);

        foreach (var element in dictionary.Elements)
        {
            var masked = Mask(element.Value);
            changed |= !ReferenceEquals(masked, element.Value);
            elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(element.Key, masked));
        }

        return changed ? new DictionaryValue(elements) : dictionary;
    }
}
