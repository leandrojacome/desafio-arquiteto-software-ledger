using System.ComponentModel.DataAnnotations;
using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Health;

internal sealed class WorkerHealthOptions
{
    public const string SectionName = "Resilience:Health";

    [Range(10, 3600)] public int OutboxHeartbeatSeconds { get; init; } = 120;

    [Range(1, 1440)] public int IntegrityHeartbeatMinutes { get; init; } = 30;

    [Range(5, 3600)] public int OutboxLagSeconds { get; init; } = 60;
}

internal sealed class WorkerHealthOptionsValidator : IValidateOptions<WorkerHealthOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerHealthOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, WorkerHealthOptions.SectionName, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
