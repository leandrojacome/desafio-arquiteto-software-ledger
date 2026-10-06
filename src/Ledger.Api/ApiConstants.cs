namespace Ledger.Api;

internal static class ApiConstants
{
    public const string ServiceName = "ledger-api";

    public const int MaxRequestBodyBytes = 16 * 1024;

    public const string V1RoutePrefix = "/v1";

    public const string LiveRoute = "/health/live";

    public const string ReadyRoute = "/health/ready";

    public const string CorrelationIdHeader = "X-Correlation-Id";

    public const string IdempotentReplayedHeader = "Idempotent-Replayed";

    public const string OpenApiRoute = "/openapi/v1.json";
}
