namespace Ledger.Api.IntegrationTests.Writes.Support;

internal sealed record WriteRequestOptions
{
    public string? IdempotencyKey { get; init; }

    public string? Token { get; init; }

    public bool Anonymous { get; init; }

    public string? ContentType { get; init; } = "application/json";

    public string? CorrelationId { get; init; }

    public IReadOnlyList<string> RepeatedIdempotencyKeys { get; init; } = [];
}
