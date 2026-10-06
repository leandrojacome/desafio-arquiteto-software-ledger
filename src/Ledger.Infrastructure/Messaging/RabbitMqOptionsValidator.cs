using Ledger.Application.Configuration;
using Ledger.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Messaging;

internal sealed class RabbitMqOptionsValidator(IHostEnvironment environment) : IValidateOptions<RabbitMqOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMqOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, RabbitMqOptions.SectionName, failures);
        OptionsValidation.Collect(options.Retention, RetentionQueueOptions.SectionName, failures);

        if (options.ReconnectMinSeconds > options.ReconnectMaxSeconds)
        {
            failures.Add(
                $"{RabbitMqOptions.SectionName}:ReconnectMinSeconds: must be less than or equal to ReconnectMaxSeconds.");
        }

        if (options.Retention.Enabled && options.Retention.Queue.StartsWith("amq.", StringComparison.Ordinal))
        {
            failures.Add($"{RetentionQueueOptions.SectionName}:Queue: names starting with amq. are reserved by the broker.");
        }

        if (environment.RequiresProductionControls() && !options.UseTls)
        {
            failures.Add($"{RabbitMqOptions.SectionName}:UseTls: must be true outside Development and Testing.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
