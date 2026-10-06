using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.Security;

internal sealed class PublicKeyLoadResult
{
    private readonly SecurityKey? _key;

    private PublicKeyLoadResult(SecurityKey? key, PublicKeyProblem problem)
    {
        _key = key;
        Problem = problem;
    }

    public PublicKeyProblem Problem { get; }

    public bool IsLoaded => _key is not null;

    public SecurityKey Key => _key ?? throw new InvalidOperationException("The public key was not loaded.");

    public static PublicKeyLoadResult Success(SecurityKey key) => new(key, PublicKeyProblem.None);

    public static PublicKeyLoadResult Failure(PublicKeyProblem problem) => new(null, problem);
}
