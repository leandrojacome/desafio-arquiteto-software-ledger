using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.Security;

internal static class PublicKeyLoader
{
    private const int MinimumRsaKeySize = 2048;
    private const string PrivateKeyMarker = "PRIVATE KEY";

    public static PublicKeyLoadResult Load(string path)
    {
        string pem;

        try
        {
            pem = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PublicKeyLoadResult.Failure(PublicKeyProblem.Unreadable);
        }

        if (pem.Contains(PrivateKeyMarker, StringComparison.Ordinal))
        {
            return PublicKeyLoadResult.Failure(PublicKeyProblem.ContainsPrivateKey);
        }

        return TryRsa(pem) ?? TryEcdsa(pem) ?? PublicKeyLoadResult.Failure(PublicKeyProblem.NotAPublicKey);
    }

    private static PublicKeyLoadResult? TryRsa(string pem)
    {
        using var rsa = RSA.Create();

        try
        {
            rsa.ImportFromPem(pem);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return null;
        }

        if (rsa.KeySize < MinimumRsaKeySize)
        {
            return PublicKeyLoadResult.Failure(PublicKeyProblem.WeakKey);
        }

        return PublicKeyLoadResult.Success(new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false)));
    }

    [SuppressMessage("Reliability", "CA2000",
        Justification = "The ECDsa instance is owned by the security key for the lifetime of the process.")]
    private static PublicKeyLoadResult? TryEcdsa(string pem)
    {
        using var imported = ECDsa.Create();

        try
        {
            imported.ImportFromPem(pem);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return null;
        }

        var parameters = imported.ExportParameters(includePrivateParameters: false);

        if (!IsNistP256(parameters))
        {
            return PublicKeyLoadResult.Failure(PublicKeyProblem.WeakKey);
        }

        return PublicKeyLoadResult.Success(new ECDsaSecurityKey(ECDsa.Create(parameters)));
    }

    private static bool IsNistP256(ECParameters parameters) =>
        parameters.Curve.Oid.Value == ECCurve.NamedCurves.nistP256.Oid.Value;
}
