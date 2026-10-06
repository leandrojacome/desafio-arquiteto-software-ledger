using System.ComponentModel.DataAnnotations;
using Ledger.Application.Configuration;
using Ledger.Application.Security;
using Ledger.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Messaging;

internal sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required] public string Host { get; init; } = string.Empty;

    [Range(1, 65_535)] public int Port { get; init; } = 5672;

    [Required] public string VirtualHost { get; init; } = "/";

    [Required] public string Username { get; init; } = string.Empty;

    [Required]
    [Sensitive]
    public string Password { get; init; } = string.Empty;

    public bool UseTls { get; init; }

    [Range(1, 60)] public int ConnectTimeoutSeconds { get; init; } = 2;

    [Required] public string Exchange { get; init; } = "ledger.events";

    [Range(1, 300)] public int ReconnectMinSeconds { get; init; } = 1;

    [Range(1, 300)] public int ReconnectMaxSeconds { get; init; } = 30;

    [Range(0, 50)] public int ReconnectJitterPercent { get; init; } = 20;

    public RetentionQueueOptions Retention { get; init; } = new();
}

internal sealed class RetentionQueueOptions
{
    public const string SectionName = "RabbitMq:Retention";

    public const string DefaultQueue = "retention.ledger.entry-registered";

    public bool Enabled { get; init; } = true;

    [Required]
    [StringLength(200, MinimumLength = 3)]
    public string Queue { get; init; } = DefaultQueue;

    [Range(1, 720)] public int TtlHours { get; init; } = 24;

    [Range(1_000, 100_000_000)] public int MaxLength { get; init; } = 1_000_000;

    [Range(1, 102_400)] public int MaxMegabytes { get; init; } = 1_024;

    [Range(1, 720)] public int DeadLetterTtlHours { get; init; } = 168;

    [Range(1_000, 10_000_000)] public int DeadLetterMaxLength { get; init; } = 100_000;

    public string DeadLetterExchange => Queue + ".dlx";

    public string DeadLetterQueue => Queue + ".dead-letter";
}

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
