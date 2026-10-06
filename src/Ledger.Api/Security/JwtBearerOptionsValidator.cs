using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Security;

internal sealed class JwtBearerOptionsValidator(IOptions<JwtAuthenticationOptions> authentication)
    : IValidateOptions<JwtBearerOptions>
{
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromSeconds(30);

    public ValidateOptionsResult Validate(string? name, JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return ValidateOptionsResult.Skip;
        }

        var failures = new List<string>();
        var parameters = options.TokenValidationParameters;

        if (options.IncludeErrorDetails)
        {
            failures.Add("JwtBearer: IncludeErrorDetails must be false so the challenge never explains the rejection.");
        }

        if (options.EventsType != typeof(AuthenticationEvents))
        {
            failures.Add("JwtBearer: the authentication events that count and log rejections are not configured.");
        }

        if (parameters.ValidAlgorithms is null || !parameters.ValidAlgorithms.Any())
        {
            failures.Add("JwtBearer: the accepted signing algorithms are not restricted.");
        }

        if (!parameters.RequireSignedTokens || !parameters.RequireExpirationTime)
        {
            failures.Add("JwtBearer: tokens must be signed and carry an expiration.");
        }

        if (parameters.ClockSkew > MaximumClockSkew)
        {
            failures.Add("JwtBearer: the clock skew must not exceed 30 seconds.");
        }

        CollectKeyFailures(authentication.Value, options, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CollectKeyFailures(
        JwtAuthenticationOptions settings,
        JwtBearerOptions options,
        List<string> failures)
    {
        if (settings.Mode == AuthenticationMode.LocalKey && options.TokenValidationParameters.IssuerSigningKey is null)
        {
            failures.Add($"{JwtAuthenticationOptions.SectionName}:LocalKey: no signing key was loaded.");
        }

        if (settings.Mode == AuthenticationMode.Authority && string.IsNullOrWhiteSpace(options.Authority))
        {
            failures.Add($"{JwtAuthenticationOptions.SectionName}:Authority: the authority was not applied to the bearer options.");
        }
    }
}
