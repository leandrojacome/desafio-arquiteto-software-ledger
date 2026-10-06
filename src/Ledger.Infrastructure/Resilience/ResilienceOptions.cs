using System.ComponentModel.DataAnnotations;
using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Resilience;

public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    [Range(1, 60)] public int RequestTimeoutSeconds { get; init; } = 3;

    [Range(1, 300)] public int ShutdownTimeoutSeconds { get; init; } = 30;

    [Range(1, 60)] public int ServiceUnavailableRetryAfterSeconds { get; init; } = 1;

    public ResilienceHealthOptions Health { get; init; } = new();

    public ResilienceRetryOptions Retry { get; init; } = new();
}

public sealed class ResilienceHealthOptions
{
    [Range(1, 30)] public int ProbeTimeoutSeconds { get; init; } = 1;

    [Range(1, 60)] public int CacheSeconds { get; init; } = 5;

    [Range(1, 60)] public int RetryAfterSeconds { get; init; } = 5;
}

public sealed class ResilienceRetryOptions
{
    [Range(0, 5)] public int MaxRetryAttempts { get; init; } = 2;

    [Range(10, 1000)] public int BaseDelayMs { get; init; } = 50;
}

internal sealed class ResilienceOptionsValidator : IValidateOptions<ResilienceOptions>
{
    public ValidateOptionsResult Validate(string? name, ResilienceOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, ResilienceOptions.SectionName, failures);
        OptionsValidation.Collect(options.Health, $"{ResilienceOptions.SectionName}:Health", failures);
        OptionsValidation.Collect(options.Retry, $"{ResilienceOptions.SectionName}:Retry", failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
