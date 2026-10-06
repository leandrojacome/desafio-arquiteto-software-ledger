using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Ledger.Infrastructure.Persistence;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class NpgsqlTestRuntime
{
    [SuppressMessage("Usage", "CA2255",
        Justification = "The test process must run with the Npgsql switches of the executables before any data source exists.")]
    [ModuleInitializer]
    internal static void Initialize() => NpgsqlRuntimeSwitches.Apply();
}
