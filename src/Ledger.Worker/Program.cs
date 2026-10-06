using Ledger.Application;
using Ledger.Infrastructure;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Persistence;
using Ledger.Worker;
using Microsoft.Extensions.Options;
using Serilog.Extensions.Logging;

const int InvalidConfigurationExitCode = 3;

var builder = WebApplication.CreateBuilder(args);

var isMigration = args.Contains(WorkerConstants.MigrateArgument);
var inspectedAccount = args
    .FirstOrDefault(argument => argument.StartsWith(InspectAccountCommand.Argument, StringComparison.Ordinal))
    ?.Split('=', 2)
    .ElementAtOrDefault(1);
var isInspection = inspectedAccount is not null || args.Contains(InspectAccountCommand.Argument);

builder.Services
    .AddInfrastructure(
        builder.Configuration,
        isMigration ? PostgresSource.Migrator : PostgresSource.Worker)
    .AddLedgerLogging(WorkerConstants.ServiceName)
    .AddLedgerTelemetry(WorkerConstants.ServiceName);

if (!isMigration)
{
    builder.Services.AddApplication();
}

if (!isMigration && !isInspection)
{
    builder.Services.AddLedgerWorker(builder.Configuration);
}

await using var app = builder.Build();

if (isMigration)
{
    return await MigrateCommand.RunAsync(app.Services, CancellationToken.None);
}

if (isInspection)
{
    return await InspectAccountCommand.RunAsync(app.Services, inspectedAccount, CancellationToken.None);
}

app.MapHealthEndpoints();

try
{
    await app.RunAsync();
}
catch (OptionsValidationException exception)
{
    using var startupLogger = StartupLogger.Create(WorkerConstants.ServiceName);
    using var loggerFactory = new SerilogLoggerFactory(startupLogger);

    var failures = string.Join("; ", exception.Failures);

    var logger = loggerFactory.CreateLogger(WorkerConstants.ServiceName);

    WorkerLog.WorkerConfigurationInvalid(logger, failures);

    return InvalidConfigurationExitCode;
}

return 0;
