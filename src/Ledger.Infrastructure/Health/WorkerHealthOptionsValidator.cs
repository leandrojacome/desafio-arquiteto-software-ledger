using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Health;

internal sealed class WorkerHealthOptionsValidator : IValidateOptions<WorkerHealthOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerHealthOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, WorkerHealthOptions.SectionName, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
