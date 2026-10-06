using Ledger.Infrastructure.Hosting;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Security;

internal sealed class JwtAuthenticationOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<JwtAuthenticationOptions>
{
    private const int MinimumSigningKeyLength = 32;

    private const int MaximumLifetimeCeilingMinutes = 1440;

    private const int MaximumLifetimeInProductionMinutes = 60;

    private const string Section = JwtAuthenticationOptions.SectionName;

    public ValidateOptionsResult Validate(string? name, JwtAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add($"{Section}:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"{Section}:Audience is required.");
        }

        ValidateLifetimeCeiling(options.MaxTokenLifetimeMinutes, failures);

        switch (options.Mode)
        {
            case AuthenticationMode.Authority:
                ValidateAuthority(options, failures);
                break;
            case AuthenticationMode.LocalKey:
                ValidateLocalKey(options.LocalKey, failures);
                break;
            default:
                failures.Add($"{Section}:Mode must be Authority or LocalKey.");
                break;
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateLifetimeCeiling(int minutes, List<string> failures)
    {
        const string key = $"{Section}:MaxTokenLifetimeMinutes";

        if (minutes is < 1 or > MaximumLifetimeCeilingMinutes)
        {
            failures.Add($"{key} must be between 1 and {MaximumLifetimeCeilingMinutes}.");
        }
        else if (minutes > MaximumLifetimeInProductionMinutes && environment.RequiresProductionControls())
        {
            failures.Add($"{key} must not exceed {MaximumLifetimeInProductionMinutes} outside Development and Testing.");
        }
    }

    private void ValidateAuthority(JwtAuthenticationOptions options, List<string> failures)
    {
        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority))
        {
            failures.Add($"{Section}:Authority must be an absolute URI when Mode is Authority.");

            return;
        }

        if (!environment.RequiresProductionControls())
        {
            return;
        }

        if (authority.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add($"{Section}:Authority must use https outside Development and Testing.");
        }

        if (!options.RequireHttpsMetadata)
        {
            failures.Add($"{Section}:RequireHttpsMetadata must be true outside Development and Testing.");
        }
    }

    private void ValidateLocalKey(LocalKeyOptions localKey, List<string> failures)
    {
        if (environment.RequiresProductionControls())
        {
            failures.Add($"{Section}:Mode LocalKey is not allowed outside Development and Testing.");
        }

        var signingKey = localKey.SigningKey;
        var publicKeyPath = localKey.PublicKeyPath;
        var hasSigningKey = !string.IsNullOrWhiteSpace(signingKey);
        var hasPublicKeyPath = !string.IsNullOrWhiteSpace(publicKeyPath);

        if (!hasSigningKey && !hasPublicKeyPath)
        {
            failures.Add($"{Section}:LocalKey requires SigningKey or PublicKeyPath.");

            return;
        }

        if (!string.IsNullOrWhiteSpace(publicKeyPath))
        {
            ValidatePublicKey(publicKeyPath, failures);
        }
        else if (signingKey is { Length: < MinimumSigningKeyLength })
        {
            failures.Add($"{Section}:LocalKey:SigningKey must have at least {MinimumSigningKeyLength} characters.");
        }
    }

    private static void ValidatePublicKey(string path, List<string> failures)
    {
        const string key = $"{Section}:LocalKey:PublicKeyPath";

        if (!File.Exists(path))
        {
            failures.Add($"{key} must point to an existing file.");

            return;
        }

        var loaded = PublicKeyLoader.Load(path);

        switch (loaded.Problem)
        {
            case PublicKeyProblem.None:
                break;
            case PublicKeyProblem.Unreadable:
                failures.Add($"{key} could not be read.");
                break;
            case PublicKeyProblem.ContainsPrivateKey:
                failures.Add($"{key} must hold a public key, not a private key.");
                break;
            case PublicKeyProblem.WeakKey:
                failures.Add($"{key} must hold an RSA key of at least 2048 bits or a P-256 EC key.");
                break;
            default:
                failures.Add($"{key} must hold a PEM encoded RSA or P-256 EC public key.");
                break;
        }
    }
}
