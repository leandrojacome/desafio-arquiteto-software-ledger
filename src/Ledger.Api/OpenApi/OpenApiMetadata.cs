namespace Ledger.Api.OpenApi;

internal sealed record IdempotencyKeyMetadata(bool IsRequired);

internal sealed record ProblemCodesMetadata(int Status, IReadOnlyList<string> Codes);

internal sealed record RequestBodyMetadata(Type ContentType, bool IsRequired);

internal sealed record QueryParameterMetadata(
    string Name,
    string Description,
    QueryParameterKind Kind,
    int? Minimum = null,
    int? Maximum = null,
    int? DefaultValue = null,
    int? MaxLength = null);

internal enum QueryParameterKind
{
    Instant = 1,
    Integer = 2,
    Text = 3
}
