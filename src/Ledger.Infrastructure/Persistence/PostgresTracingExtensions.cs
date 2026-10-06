using Npgsql;
using OpenTelemetry.Trace;

namespace Ledger.Infrastructure.Persistence;

internal static class PostgresTracingExtensions
{
    public static TracerProviderBuilder AddPostgresTracing(this TracerProviderBuilder builder) => builder.AddNpgsql();
}
