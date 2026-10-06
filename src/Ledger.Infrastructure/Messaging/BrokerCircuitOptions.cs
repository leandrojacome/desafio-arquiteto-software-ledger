using System.ComponentModel.DataAnnotations;
using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Messaging;

internal sealed class BrokerCircuitOptions
{
    public const string SectionName = "Resilience:BrokerCircuitBreaker";

    [Range(0.1, 1.0)] public double FailureRatio { get; init; } = 0.5;

    [Range(5, 300)] public int SamplingSeconds { get; init; } = 30;

    [Range(2, 1000)] public int MinimumThroughput { get; init; } = 10;

    [Range(5, 600)] public int BreakSeconds { get; init; } = 30;
}

internal sealed class BrokerCircuitOptionsValidator : IValidateOptions<BrokerCircuitOptions>
{
    public ValidateOptionsResult Validate(string? name, BrokerCircuitOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, BrokerCircuitOptions.SectionName, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
