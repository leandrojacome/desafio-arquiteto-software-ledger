using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Resilience;

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
