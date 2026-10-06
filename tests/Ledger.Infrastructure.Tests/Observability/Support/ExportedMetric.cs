namespace Ledger.Infrastructure.Tests.Observability.Support;

internal sealed record ExportedMetric(string Name, string? Unit, IReadOnlyList<double> Boundaries);
