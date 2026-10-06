using System.Text.Json.Nodes;
using Ledger.Api.Contracts;
using Ledger.Api.ErrorHandling;
using Ledger.Domain.Entries;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ledger.Api.OpenApi;

internal sealed class LedgerSchemaTransformer : IOpenApiSchemaTransformer
{
    private const string CodeProperty = "code";
    private const string TypeProperty = "type";
    private const string CurrencyProperty = "currency";
    private const string SettledProperty = "settled";
    private const string SupportedCurrency = "BRL";

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        var type = context.JsonTypeInfo.Type;

        if (type == typeof(ProblemResponse))
        {
            Restrict(schema, CodeProperty, ProblemCatalog.Codes.Order(StringComparer.Ordinal));
        }
        else if (type == typeof(RegisterEntryRequest) || type == typeof(EntryResponse))
        {
            Restrict(schema, TypeProperty, [EntryType.Credit.ToDatabaseText(), EntryType.Debit.ToDatabaseText()]);
        }
        else if (type == typeof(CreateAccountRequest))
        {
            Restrict(schema, CurrencyProperty, [SupportedCurrency]);
        }
        else if (type == typeof(BalanceResponse))
        {
            schema.Required?.Remove(SettledProperty);
        }

        return Task.CompletedTask;
    }

    private static void Restrict(OpenApiSchema schema, string property, IEnumerable<string> values)
    {
        if (schema.Properties is { } properties
            && properties.TryGetValue(property, out var candidate)
            && candidate is OpenApiSchema target)
        {
            target.Enum = [.. values.Select(ToNode)];
        }
    }

    private static JsonNode ToNode(string value) =>
        JsonValue.Create(value) ?? throw new InvalidOperationException("The value could not be written as JSON.");
}
