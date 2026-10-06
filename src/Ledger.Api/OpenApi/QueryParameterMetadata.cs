namespace Ledger.Api.OpenApi;

internal sealed record QueryParameterMetadata(
    string Name,
    string Description,
    QueryParameterKind Kind,
    int? Minimum = null,
    int? Maximum = null,
    int? DefaultValue = null,
    int? MaxLength = null);
