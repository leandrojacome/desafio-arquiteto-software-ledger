using Ledger.Application.Configuration;
using Ledger.Infrastructure.Hosting;
using Microsoft.Extensions.Options;

namespace Ledger.Api.RateLimiting;

internal sealed class RateLimitingOptionsValidator(IHostEnvironment environment, IConfiguration configuration)
    : IValidateOptions<RateLimitingOptions>
{
    private const int MaximumPoolMultiple = 3;

    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!options.Enabled && environment.RequiresProductionControls())
        {
            failures.Add($"{RateLimitingOptions.SectionName}:{nameof(RateLimitingOptions.Enabled)} must be true outside Development and Testing.");
        }

        if (options.ReplenishmentSeconds != 1 && environment.RequiresProductionControls())
        {
            failures.Add($"{RateLimitingOptions.SectionName}:{nameof(RateLimitingOptions.ReplenishmentSeconds)} must be 1 outside Development and Testing.");
        }

        CollectFailures(options.WritePerClient, nameof(RateLimitingOptions.WritePerClient), failures);
        CollectFailures(options.ReadPerClient, nameof(RateLimitingOptions.ReadPerClient), failures);
        CollectFailures(options.WritePerAccount, nameof(RateLimitingOptions.WritePerAccount), failures);

        CollectConcurrencyFailures(options.WriteConcurrency, nameof(RateLimitingOptions.WriteConcurrency), "Write", failures);
        CollectConcurrencyFailures(options.BalanceConcurrency, nameof(RateLimitingOptions.BalanceConcurrency), "Balance", failures);
        CollectConcurrencyFailures(options.StatementConcurrency, nameof(RateLimitingOptions.StatementConcurrency), "Statement", failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CollectFailures(TokenBucketSettings bucket, string name, List<string> failures)
    {
        var path = $"{RateLimitingOptions.SectionName}:{name}";

        OptionsValidation.Collect(bucket, path, failures);

        if (bucket.RefillPerSecond > bucket.Capacity)
        {
            failures.Add(
                $"{path}: {nameof(TokenBucketSettings.RefillPerSecond)} must not exceed {nameof(TokenBucketSettings.Capacity)}.");
        }
    }

    private void CollectConcurrencyFailures(int limit, string name, string source, List<string> failures)
    {
        var path = $"{RateLimitingOptions.SectionName}:{name}";
        var poolKey = $"Postgres:Sources:{source}:MaxPoolSize";

        if (limit is < 1 or > 256 || !int.TryParse(configuration[poolKey], out var pool) || pool < 1)
        {
            return;
        }

        if (limit < pool || limit > pool * MaximumPoolMultiple)
        {
            failures.Add($"{path}: must be between one and three times {poolKey}.");
        }
    }
}
