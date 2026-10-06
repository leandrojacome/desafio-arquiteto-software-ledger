using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Worker;

internal sealed class IntegrityOptionsValidator : IValidateOptions<IntegrityOptions>
{
    public ValidateOptionsResult Validate(string? name, IntegrityOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, IntegrityOptions.SectionName, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
