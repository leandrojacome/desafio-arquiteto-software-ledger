using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ledger.EndToEnd.Tests.Support;

internal static class E2ETokenFactory
{
    public const string ReadAndWrite = "ledger.read ledger.write";

    public const string ReadOnly = "ledger.read";

    public const string WriteOnly = "ledger.write";

    public const string DefaultAudience = "ledger-api";

    private const string SigningKeyVariable = "LEDGER_E2E_SIGNING_KEY_PATH";

    private const string Issuer = "https://idp.local.test";

    private const string KeyId = "dev-local";

    private const string DefaultClient = "e2e-client";

    private const string RelativeKeyPath = "dev-keys/private.pem";

    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(30);

    private static readonly Lazy<RSA> FileKey = new(LoadKey);

    private static RSA? _generatedKey;

    public static string UseGeneratedKey()
    {
        var generated = RSA.Create(2048);
        _generatedKey = generated;

        return Convert.ToBase64String(Encoding.ASCII.GetBytes(generated.ExportSubjectPublicKeyInfoPem()));
    }

    public static string Create(
        string scope = ReadAndWrite,
        string client = DefaultClient,
        TimeSpan? lifetime = null,
        TimeSpan? age = null,
        string audience = DefaultAudience,
        RSA? signer = null)
    {
        var now = TimeProvider.System.GetUtcNow();
        var issuedAt = now - (age ?? TimeSpan.Zero);
        var header = new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "at+jwt", ["kid"] = KeyId };
        var payload = new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = audience,
            ["sub"] = client,
            ["client_id"] = client,
            ["scope"] = scope,
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["nbf"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = issuedAt.Add(lifetime ?? DefaultLifetime).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString()
        };

        var signingInput = $"{Encode(JsonSerializer.SerializeToUtf8Bytes(header))}.{Encode(JsonSerializer.SerializeToUtf8Bytes(payload))}";
        var signature = (signer ?? _generatedKey ?? FileKey.Value).SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Encode(signature)}";
    }

    public static string CreateExpired() =>
        Create(age: TimeSpan.FromHours(2), lifetime: TimeSpan.FromHours(1));

    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static RSA LoadKey()
    {
        var path = Environment.GetEnvironmentVariable(SigningKeyVariable);

        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(E2EPaths.RepositoryRoot(), RelativeKeyPath);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The development signing key was not found at {path}. Create it with 'openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out dev-keys/private.pem' as the README describes, point {SigningKeyVariable} at the private key the stack trusts, or set LEDGER_E2E_PROVISION=true so the tests generate their own key pair.");
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(path));

        return rsa;
    }
}
