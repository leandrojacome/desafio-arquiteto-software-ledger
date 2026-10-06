using Ledger.Api.Contracts;
using Ledger.Api.ErrorHandling;
using Ledger.Api.Middleware;
using Ledger.Api.OpenApi;
using Ledger.Api.RateLimiting;
using Ledger.Api.Reads;
using Ledger.Api.Security;
using Ledger.Api.Validation;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;

namespace Ledger.Api.Endpoints;

internal static class StatementEndpoints
{
    private const string Route = "/accounts/{accountId}/entries";

    public static void MapStatementEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet(Route, ListEntriesAsync)
            .RequireAuthorization(AuthorizationPolicies.LedgerRead)
            .WithRequestClass(RequestClass.Statement)
            .WithName("ListEntries")
            .WithTags(LedgerDocumentTransformer.EntriesTag)
            .WithSummary("Devolve o extrato de uma conta, do lançamento mais recente para o mais antigo, em páginas.")
            .WithDescription(
                "Os lançamentos são ordenados pelo instante do registro e, em caso de empate, pela versão da conta. " +
                "from é inclusivo e to é exclusivo, ambos instantes ISO 8601 com fuso horário informado. " +
                "limit vai de 1 até o máximo configurado. nextCursor é assinado, vinculado à conta e nulo na " +
                "última página.")
            .WithQueryParameter(new QueryParameterMetadata(
                StatementQueryReader.FromName,
                "Limite inferior inclusivo do instante do registro, como instante ISO 8601 com fuso horário " +
                "informado, por exemplo Z ou -03:00. Na URL, o deslocamento positivo deve ser escrito como %2B, " +
                "porque o + vira espaço.",
                QueryParameterKind.Instant))
            .WithQueryParameter(new QueryParameterMetadata(
                StatementQueryReader.ToName,
                "Limite superior exclusivo do instante do registro, como instante ISO 8601 com fuso horário " +
                "informado, por exemplo Z ou -03:00. Deve ser posterior a from. Na URL, o deslocamento positivo " +
                "deve ser escrito como %2B, porque o + vira espaço.",
                QueryParameterKind.Instant))
            .WithQueryParameter(new QueryParameterMetadata(
                StatementQueryReader.LimitName,
                "Tamanho da página. O limite superior é o máximo configurado, 200 a menos que a implantação o reduza.",
                QueryParameterKind.Integer,
                Minimum: 1,
                Maximum: StatementOptions.LimitCeiling,
                DefaultValue: StatementOptions.DefaultPageSize))
            .WithQueryParameter(new QueryParameterMetadata(
                StatementQueryReader.CursorName,
                "O nextCursor da página anterior. É assinado e vinculado à conta.",
                QueryParameterKind.Text,
                MaxLength: StatementQueryReader.MaxCursorLength))
            .Produces<StatementResponse>(StatusCodes.Status200OK)
            .ProducesLedgerProblem(StatusCodes.Status400BadRequest, ProblemCatalog.ValidationFailed)
            .ProducesLedgerProblem(StatusCodes.Status401Unauthorized, ProblemCatalog.Unauthenticated)
            .ProducesLedgerProblem(StatusCodes.Status403Forbidden, ProblemCatalog.Forbidden)
            .ProducesLedgerProblem(StatusCodes.Status404NotFound, ProblemCatalog.AccountNotFound)
            .ProducesLedgerProblem(StatusCodes.Status429TooManyRequests, ProblemCatalog.RateLimited)
            .ProducesLedgerProblem(StatusCodes.Status500InternalServerError, ProblemCatalog.InternalError)
            .ProducesLedgerProblem(StatusCodes.Status503ServiceUnavailable, ProblemCatalog.ServiceUnavailable);
    }

    private static async Task<IResult> ListEntriesAsync(
        string accountId,
        HttpContext context,
        StatementQueryReader reader,
        ListEntriesHandler handler,
        CancellationToken cancellationToken)
    {
        var account = AccountId.From(accountId);

        if (account.IsFailure)
        {
            return ReadResults.Failure(context, account.Error);
        }

        var input = reader.Read(account.Value, context.Request.QueryString.Value);

        if (!input.IsValid)
        {
            return ReadResults.Invalid(context, input.Issues);
        }

        var request = input.Value;
        var query = new ListEntriesQuery(
            account.Value,
            request.From,
            request.To,
            request.Limit,
            request.Cursor,
            ReadResults.ClientIdOf(context),
            CorrelationIdMiddleware.Resolve(context));

        var result = await handler.HandleAsync(query, cancellationToken);

        return result.IsSuccess
            ? ReadResults.Success(context, StatementResponse.From(result.Value))
            : ReadResults.Failure(context, result.Error);
    }
}
