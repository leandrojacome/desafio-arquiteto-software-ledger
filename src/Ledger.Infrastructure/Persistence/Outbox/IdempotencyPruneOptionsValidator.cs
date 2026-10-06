using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Persistence.Outbox;

internal sealed class IdempotencyPruneOptionsValidator : IValidateOptions<IdempotencyPruneOptions>
{
    public ValidateOptionsResult Validate(string? name, IdempotencyPruneOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, IdempotencyPruneOptions.SectionName, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
