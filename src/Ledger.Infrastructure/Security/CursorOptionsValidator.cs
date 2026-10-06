using System.Globalization;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Security;

internal sealed class CursorOptionsValidator : IValidateOptions<CursorOptions>
{
    public ValidateOptionsResult Validate(string? name, CursorOptions options)
    {
        if (options.DecodeSigningKey() is not null)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{CursorOptions.SigningKeyName} is required and must be the base64 of exactly {CursorOptions.SigningKeySize} bytes."));
    }
}
