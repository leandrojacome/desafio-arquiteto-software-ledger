using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Persistence;

internal sealed class BalanceReadOptionsValidator : IValidateOptions<BalanceReadOptions>
{
    public ValidateOptionsResult Validate(string? name, BalanceReadOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, BalanceReadOptions.SectionName, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
