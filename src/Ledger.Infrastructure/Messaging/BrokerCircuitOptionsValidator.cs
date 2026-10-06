using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Messaging;

internal sealed class BrokerCircuitOptionsValidator : IValidateOptions<BrokerCircuitOptions>
{
    public ValidateOptionsResult Validate(string? name, BrokerCircuitOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, BrokerCircuitOptions.SectionName, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
