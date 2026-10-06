using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Worker;

internal sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        var failures = new List<string>();
        var backoffPath = $"{WorkerOptions.SectionName}:FailureBackoff";

        OptionsValidation.Collect(options.FailureBackoff, backoffPath, failures);

        if (options.FailureBackoff.MinSeconds > options.FailureBackoff.MaxSeconds)
        {
            failures.Add($"{backoffPath}:MinSeconds: must be less than or equal to MaxSeconds.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
