using System.Globalization;
using Ledger.Application.Configuration;
using Ledger.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresOptionsValidator(
    IReadOnlyCollection<PostgresSource> requiredSources,
    IHostEnvironment environment) : IValidateOptions<PostgresOptions>
{
    public ValidateOptionsResult Validate(string? name, PostgresOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, PostgresOptions.SectionName, failures);

        if (environment.RequiresProductionControls())
        {
            CollectProductionFailures(options, failures);
        }

        foreach (var source in requiredSources)
        {
            var path = $"{PostgresOptions.SectionName}:Sources:{source}";
            var settings = options.For(source);

            OptionsValidation.Collect(settings, path, failures);
            CollectPoolFailures(settings, path, failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CollectProductionFailures(PostgresOptions options, List<string> failures)
    {
        if (options.SslMode != SslMode.VerifyFull)
        {
            failures.Add($"{PostgresOptions.SectionName}:SslMode: must be VerifyFull outside Development and Testing.");
        }

        if (options.IncludeErrorDetail)
        {
            failures.Add($"{PostgresOptions.SectionName}:IncludeErrorDetail: must be false outside Development and Testing.");
        }
    }

    private static void CollectPoolFailures(PostgresSourceOptions settings, string path, List<string> failures)
    {
        if (settings.MinPoolSize <= settings.MaxPoolSize)
        {
            return;
        }

        failures.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{path}: MinPoolSize ({settings.MinPoolSize}) must not exceed MaxPoolSize ({settings.MaxPoolSize})."));
    }
}
