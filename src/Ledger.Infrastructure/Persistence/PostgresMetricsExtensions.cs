using OpenTelemetry.Metrics;

namespace Ledger.Infrastructure.Persistence;

internal static class PostgresMetricsExtensions
{
    private const string NpgsqlMeterName = "Npgsql";

    public static MeterProviderBuilder AddPostgresMetrics(this MeterProviderBuilder builder) =>
        builder.AddMeter(NpgsqlMeterName);
}
