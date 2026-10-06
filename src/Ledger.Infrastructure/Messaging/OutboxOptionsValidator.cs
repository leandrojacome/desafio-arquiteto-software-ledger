using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Messaging;

internal sealed class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, OutboxOptions.SectionName, failures);

        if (options.LeaseSeconds <= options.ConfirmTimeoutSeconds)
        {
            failures.Add(
                $"{OutboxOptions.SectionName}:LeaseSeconds: must be greater than ConfirmTimeoutSeconds, or a message still in flight would be claimed again.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
