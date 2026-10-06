using System.ComponentModel.DataAnnotations;

namespace Ledger.Application.Configuration;

public static class OptionsValidation
{
    public static void Collect(object instance, string path, ICollection<string> failures)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(failures);

        var results = new List<ValidationResult>();

        if (Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true))
        {
            return;
        }

        foreach (var result in results)
        {
            failures.Add($"{path}: {result.ErrorMessage}");
        }
    }
}
