using Ledger.Api.Contracts;
using Ledger.Api.ErrorHandling;
using Ledger.Api.OpenApi;
using Ledger.Api.RateLimiting;
using Ledger.Api.Security;
using Ledger.Api.Validation;
using Ledger.Application.Accounts;

namespace Ledger.Api.Endpoints;

internal static class AccountsEndpoints
{
    private const string Route = "/accounts";

    public static void MapAccountEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost(Route, CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.AccountProvisioning)
            .WithRequestClass(RequestClass.Write)
            .WithName("CreateAccount")
            .WithTags(LedgerDocumentTransformer.AccountsTag)
            .WithSummary("Cria uma conta com o documento do titular protegido.")
            .WithDescription(
                "Rota administrativa e de teste local. A conta nasce com saldo zero em BRL. " +
                "O documento do titular nunca é devolvido, apenas a forma mascarada. O cabeçalho Idempotency-Key é " +
                "opcional: com ele, repetir a requisição com o mesmo corpo devolve a conta original com " +
                "Idempotent-Replayed: true, e um corpo diferente é recusado com 422. Sem ele, cada requisição " +
                "cria uma conta nova.")
            .WithIdempotencyKey(isRequired: false)
            .WithRequestBody<CreateAccountRequest>(isRequired: true)
            .Produces<CreateAccountResponse>(StatusCodes.Status201Created)
            .ProducesLedgerProblem(StatusCodes.Status400BadRequest, ProblemCatalog.ValidationFailed)
            .ProducesLedgerProblem(StatusCodes.Status401Unauthorized, ProblemCatalog.Unauthenticated)
            .ProducesLedgerProblem(StatusCodes.Status403Forbidden, ProblemCatalog.Forbidden)
            .ProducesLedgerProblem(StatusCodes.Status413PayloadTooLarge, ProblemCatalog.PayloadTooLarge)
            .ProducesLedgerProblem(StatusCodes.Status415UnsupportedMediaType, ProblemCatalog.UnsupportedMediaType)
            .ProducesLedgerProblem(StatusCodes.Status422UnprocessableEntity, ProblemCatalog.IdempotencyKeyReused)
            .ProducesLedgerProblem(StatusCodes.Status429TooManyRequests, ProblemCatalog.RateLimited)
            .ProducesLedgerProblem(StatusCodes.Status500InternalServerError, ProblemCatalog.InternalError)
            .ProducesLedgerProblem(StatusCodes.Status503ServiceUnavailable, ProblemCatalog.ServiceUnavailable);
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        CreateAccountHandler handler,
        CancellationToken cancellationToken)
    {
        var body = await RequestBodyReader.ReadAsync(context.Request, cancellationToken);

        if (body.IsTooLarge)
        {
            return WriteResults.PayloadTooLarge(context);
        }

        if (!JsonContentType.IsAccepted(context.Request.ContentType))
        {
            return WriteResults.UnsupportedMediaType(context);
        }

        var input = CreateAccountRequestReader.Read(body.Bytes);
        var key = IdempotencyKeyReader.Read(context.Request.Headers);
        var keyIssue = KeyIssueOf(context.Request, key);

        if (!input.IsValid || keyIssue is not null)
        {
            return WriteResults.Invalid(context, WriteResults.Combine(input.Issues, keyIssue));
        }

        var command = new CreateAccountCommand(
            input.Value.HolderDocument,
            input.Value.Currency,
            input.Value.OverdraftLimit,
            WriteResults.ClientIdOf(context),
            WriteResults.CorrelationIdOf(context),
            key.IsMissing ? null : key.Key);

        var result = await handler.HandleAsync(command, cancellationToken);

        return result.IsSuccess
            ? WriteResults.CreatedAccount(context, result.Value)
            : WriteResults.Failure(context, result.Error);
    }

    private static ValidationIssue? KeyIssueOf(HttpRequest request, IdempotencyKeyReading key)
    {
        if (key.Issue is not null)
        {
            return key.Issue;
        }

        return key.IsMissing && request.Headers.ContainsKey(IdempotencyKeyReader.HeaderName)
            ? FieldIssues.InvalidFormat(IdempotencyKeyReader.HeaderName)
            : null;
    }
}
