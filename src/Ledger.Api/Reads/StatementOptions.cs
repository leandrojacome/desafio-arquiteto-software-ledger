using System.ComponentModel.DataAnnotations;
using Ledger.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Reads;

internal sealed class StatementOptions
{
    public const string SectionName = "Ledger:Statement";
    public const int LimitCeiling = 200;
    public const int DefaultPageSize = 50;

    [Range(1, LimitCeiling)] public int DefaultLimit { get; init; } = DefaultPageSize;

    [Range(1, LimitCeiling)] public int MaxLimit { get; init; } = LimitCeiling;
}

internal sealed class StatementOptionsValidator : IValidateOptions<StatementOptions>
{
    public ValidateOptionsResult Validate(string? name, StatementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        OptionsValidation.Collect(options, StatementOptions.SectionName, failures);

        if (options.DefaultLimit > options.MaxLimit)
        {
            failures.Add(
                $"{StatementOptions.SectionName}: {nameof(StatementOptions.DefaultLimit)} must not exceed {nameof(StatementOptions.MaxLimit)}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
