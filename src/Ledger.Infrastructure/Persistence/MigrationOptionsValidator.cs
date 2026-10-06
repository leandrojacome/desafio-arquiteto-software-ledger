using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Persistence;

internal sealed class MigrationOptionsValidator : IValidateOptions<MigrationOptions>
{
    public ValidateOptionsResult Validate(string? name, MigrationOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, MigrationOptions.SectionName, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
