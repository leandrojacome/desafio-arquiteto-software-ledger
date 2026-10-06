using Ledger.Api;
using Ledger.Api.Endpoints;
using Ledger.Api.ErrorHandling;
using Ledger.Api.Health;
using Ledger.Api.OpenApi;
using Ledger.Api.RateLimiting;
using Ledger.Api.Reads;
using Ledger.Api.Security;
using Ledger.Application;
using Ledger.Infrastructure;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = ApiConstants.MaxRequestBodyBytes;
});

try
{
    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration, PostgresSource.Write, PostgresSource.Balance, PostgresSource.Statement)
        .AddLedgerLogging(ApiConstants.ServiceName)
        .AddLedgerTelemetry(
            ApiConstants.ServiceName,
            tracing => tracing.AddAspNetCoreInstrumentation(),
            metrics => metrics.AddAspNetCoreInstrumentation())
        .AddLedgerJson()
        .AddLedgerProblemDetails()
        .AddLedgerOpenApi()
        .AddLedgerRequestTimeouts()
        .AddLedgerAuthentication()
        .AddLedgerDataProtection()
        .AddLedgerForwardedHeaders()
        .AddLedgerWrites()
        .AddLedgerReads()
        .AddLedgerApiHealth()
        .AddLedgerRateLimiting();

    var app = builder.Build();

    app.UseLedgerPipeline();
    app.MapLedgerEndpoints();

    await app.RunAsync();
}
catch (OptionsValidationException exception) when (StartupFailureReporter.OwnsTheProcess)
{
    return StartupFailureReporter.ReportInvalidConfiguration(exception.Failures);
}
catch (AggregateException exception)
    when (StartupFailureReporter.OwnsTheProcess && StartupFailureReporter.FailuresOf(exception) is { } failures)
{
    return StartupFailureReporter.ReportInvalidConfiguration(failures);
}

return 0;
