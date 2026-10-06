namespace Ledger.Api.IntegrationTests.Observability;

internal sealed record ExportedMetric(string Name, string? Unit, IReadOnlyList<double> Boundaries);
