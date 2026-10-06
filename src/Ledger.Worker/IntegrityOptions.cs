using System.ComponentModel.DataAnnotations;
using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Worker;

internal sealed class IntegrityOptions
{
    public const string SectionName = "Integrity";

    [Range(1, 60)] public int RecentIntervalMinutes { get; init; } = 5;

    [Range(1, 60)] public int RecentOverlapMinutes { get; init; } = 1;

    [Range(1, 168)] public int FullIntervalHours { get; init; } = 24;

    [Range(100, 20_000)] public int HeadBatchSize { get; init; } = 5000;

    [Range(1, 60)] public int ChainSliceMinutes { get; init; } = 10;
}

internal sealed class IntegrityOptionsValidator : IValidateOptions<IntegrityOptions>
{
    public ValidateOptionsResult Validate(string? name, IntegrityOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, IntegrityOptions.SectionName, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
