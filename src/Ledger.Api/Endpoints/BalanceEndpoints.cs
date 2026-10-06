using Ledger.Api.Contracts;
using Ledger.Api.ErrorHandling;
using Ledger.Api.Middleware;
using Ledger.Api.OpenApi;
using Ledger.Api.RateLimiting;
using Ledger.Api.Security;
using Ledger.Api.Validation;
using Ledger.Application.Balances;
using Ledger.Domain.Accounts;

namespace Ledger.Api.Endpoints;

internal static class BalanceEndpoints
{
    private const string Route = "/accounts/{accountId}/balance";

    public static void MapBalanceEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet(Route, GetBalanceAsync)
            .RequireAuthorization(AuthorizationPolicies.LedgerRead)
            .WithRequestClass(RequestClass.Balance)
            .WithName("GetBalance")
            .WithTags(LedgerDocumentTransformer.BalancesTag)
            .WithSummary("Devolve o saldo de uma conta, agora ou em um instante.")
            .WithDescription(
                "Sem parâmetros, a resposta é o saldo atual. Com asOf, um instante ISO 8601 com fuso horário " +
                "informado, como Z ou -03:00, a resposta é o saldo registrado até esse instante, e settled " +
                "indica se o instante já está fora da janela de acomodação.")
            .WithQueryParameter(new QueryParameterMetadata(
                BalanceQueryReader.AsOfName,
                "Instante ISO 8601 com fuso horário informado, que não esteja no futuro. O saldo é o registrado " +
                "até esse instante. O horário de Brasília é -03:00 o ano todo, sem horário de verão desde 2019. " +
                "Na URL, o deslocamento positivo deve ser escrito como %2B, porque o + vira espaço.",
                QueryParameterKind.Instant))
            .Produces<BalanceResponse>(StatusCodes.Status200OK)
            .ProducesLedgerProblem(
                StatusCodes.Status400BadRequest,
                ProblemCatalog.ValidationFailed,
                ProblemCatalog.InvalidAsOf)
            .ProducesLedgerProblem(StatusCodes.Status401Unauthorized, ProblemCatalog.Unauthenticated)
            .ProducesLedgerProblem(StatusCodes.Status403Forbidden, ProblemCatalog.Forbidden)
            .ProducesLedgerProblem(StatusCodes.Status404NotFound, ProblemCatalog.AccountNotFound)
            .ProducesLedgerProblem(StatusCodes.Status429TooManyRequests, ProblemCatalog.RateLimited)
            .ProducesLedgerProblem(StatusCodes.Status500InternalServerError, ProblemCatalog.InternalError)
            .ProducesLedgerProblem(StatusCodes.Status503ServiceUnavailable, ProblemCatalog.ServiceUnavailable);
    }

    private static async Task<IResult> GetBalanceAsync(
        string accountId,
        HttpContext context,
        GetBalanceHandler handler,
        CancellationToken cancellationToken)
    {
        var account = AccountId.From(accountId);

        if (account.IsFailure)
        {
            return ReadResults.Failure(context, account.Error);
        }

        var input = BalanceQueryReader.Read(context.Request.QueryString.Value);

        if (input.Error is { } rejection)
        {
            return ReadResults.Failure(context, rejection);
        }

        if (!input.IsValid)
        {
            return ReadResults.Invalid(context, input.Issues);
        }

        var query = new GetBalanceQuery(
            account.Value,
            input.AsOf,
            ReadResults.ClientIdOf(context),
            CorrelationIdMiddleware.Resolve(context));

        var result = await handler.HandleAsync(query, cancellationToken);

        return result.IsSuccess
            ? ReadResults.Success(context, BalanceResponse.From(result.Value))
            : ReadResults.Failure(context, result.Error);
    }
}
