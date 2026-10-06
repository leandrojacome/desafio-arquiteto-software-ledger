using System.Globalization;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Security;

internal sealed class PiiOptionsValidator : IValidateOptions<PiiOptions>
{
    public const string ActiveVersionKey = $"{PiiOptions.SectionName}:ActiveKeyVersion";

    public ValidateOptionsResult Validate(string? name, PiiOptions options)
    {
        var failures = new List<string>();

        CheckRange(failures, ActiveVersionKey, options.ActiveKeyVersion, 1, ushort.MaxValue);
        CheckRange(
            failures,
            $"{PiiOptions.SectionName}:ReloadMinutes",
            options.ReloadMinutes,
            PiiOptions.MinReloadMinutes,
            PiiOptions.MaxReloadMinutes);
        CheckRange(
            failures,
            $"{PiiOptions.SectionName}:Rewrap:BatchSize",
            options.Rewrap.BatchSize,
            RewrapOptions.MinBatchSize,
            RewrapOptions.MaxBatchSize);
        CheckRange(
            failures,
            $"{PiiOptions.SectionName}:Rewrap:IdleSeconds",
            options.Rewrap.IdleSeconds,
            RewrapOptions.MinIdleSeconds,
            RewrapOptions.MaxIdleSeconds);

        if (options.Provider == PiiProvider.Directory && string.IsNullOrWhiteSpace(options.Directory))
        {
            failures.Add($"{PiiOptions.SectionName}:Directory is required when the provider is Directory.");
        }

        if (options.Provider == PiiProvider.Configuration && failures.Count == 0)
        {
            CollectKeySetFailures(options, failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckRange(List<string> failures, string key, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add(
                string.Create(CultureInfo.InvariantCulture, $"{key} must be between {minimum} and {maximum}."));
        }
    }

    private static void CollectKeySetFailures(PiiOptions options, List<string> failures)
    {
        IReadOnlyList<RawKeySet> raws;

        try
        {
            raws = ConfigurationKeySets.ToRawSets(options);
        }
        catch (KeyMaterialRejectedException exception)
        {
            failures.Add(exception.Message);

            return;
        }

        foreach (var raw in raws)
        {
            try
            {
                KeySetReader.Read(raw);
            }
            catch (KeyMaterialRejectedException exception)
            {
                failures.Add(exception.Message);
            }
        }

        if (failures.Count == 0 && raws.All(raw => raw.Version != options.ActiveKeyVersion))
        {
            failures.Add(KeySetReader.NoActiveSetMessage((ushort)options.ActiveKeyVersion, ActiveVersionKey));
        }
    }
}
