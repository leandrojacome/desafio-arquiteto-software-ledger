namespace Ledger.Api.OpenApi;

internal sealed record ProblemCodesMetadata(int Status, IReadOnlyList<string> Codes);
