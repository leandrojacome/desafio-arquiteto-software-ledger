using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Ledger.Infrastructure.Persistence;

internal static class PostgresMetricsExtensions
{
    private const string NpgsqlMeterName = "Npgsql";

    public static MeterProviderBuilder AddPostgresMetrics(this MeterProviderBuilder builder) =>
        builder.AddMeter(NpgsqlMeterName);
}

internal static class PostgresTracingExtensions
{
    public static TracerProviderBuilder AddPostgresTracing(this TracerProviderBuilder builder) => builder.AddNpgsql();
}
