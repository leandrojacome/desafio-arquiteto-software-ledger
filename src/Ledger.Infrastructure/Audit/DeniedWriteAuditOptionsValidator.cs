using System.Globalization;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Audit;

internal sealed class DeniedWriteAuditOptionsValidator : IValidateOptions<DeniedWriteAuditOptions>
{
    public ValidateOptionsResult Validate(string? name, DeniedWriteAuditOptions options)
    {
        var failures = new List<string>();

        CheckRange(failures, nameof(DeniedWriteAuditOptions.Capacity), options.Capacity);
        CheckRange(failures, nameof(DeniedWriteAuditOptions.RefillPerSecond), options.RefillPerSecond);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckRange(List<string> failures, string property, int value)
    {
        if (value is >= DeniedWriteAuditOptions.MinValue and <= DeniedWriteAuditOptions.MaxValue)
        {
            return;
        }

        failures.Add(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{DeniedWriteAuditOptions.SectionName}:{property} must be between {DeniedWriteAuditOptions.MinValue} and {DeniedWriteAuditOptions.MaxValue}."));
    }
}
