using Ledger.Application;

namespace Ledger.Worker;

internal static class WorkerConstants
{
    public const string ServiceName = ServiceIdentity.Worker;

    public const string MigrateArgument = "--migrate";

    public const string LiveRoute = "/health/live";

    public const string ReadyRoute = "/health/ready";
}
