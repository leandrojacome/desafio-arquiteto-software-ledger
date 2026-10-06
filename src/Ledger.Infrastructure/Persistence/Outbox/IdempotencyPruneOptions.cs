using System.ComponentModel.DataAnnotations;
using Ledger.Application.Configuration;
using Ledger.Application.Idempotency;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Persistence.Outbox;

internal sealed class IdempotencyPruneOptions
{
    public const string SectionName = "Idempotency";

    [Range(1, 365)] public int RetentionDays { get; init; } = 35;

    [Range(1, 1440)] public int PruneIntervalMinutes { get; init; } = 10;

    [Range(100, 50_000)] public int PruneBatchSize { get; init; } = 5000;

    public IdempotencyPruneSettings ToSettings() =>
        new(TimeSpan.FromDays(RetentionDays), PruneBatchSize, TimeSpan.FromMinutes(PruneIntervalMinutes));
}

internal sealed class IdempotencyPruneOptionsValidator : IValidateOptions<IdempotencyPruneOptions>
{
    public ValidateOptionsResult Validate(string? name, IdempotencyPruneOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, IdempotencyPruneOptions.SectionName, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
