using Ledger.Api.Contracts;
using Ledger.Api.ErrorHandling;
using Ledger.Api.OpenApi;
using Ledger.Api.RateLimiting;
using Ledger.Api.Security;
using Ledger.Api.Validation;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Api.Endpoints;

internal static class EntriesEndpoints
{
    private const string RegisterRoute = "/accounts/{accountId}/entries";
    private const string ReverseRoute = "/accounts/{accountId}/entries/{entryId}/reversals";

    public static void MapEntryEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost(RegisterRoute, RegisterAsync)
            .RequireAuthorization(AuthorizationPolicies.LedgerWrite)
            .WithRequestClass(RequestClass.Write)
            .WithName("RegisterEntry")
            .WithTags(LedgerDocumentTransformer.EntriesTag)
            .WithSummary("Registra um crédito ou um débito em uma conta.")
            .WithDescription(
                "Exige o cabeçalho Idempotency-Key. Repetir a requisição com a mesma chave e o mesmo conteúdo " +
                "devolve o lançamento original com Idempotent-Replayed: true. Um débito que a conta não cobre " +
                "é recusado com 422 e não consome a chave.")
            .WithIdempotencyKey(isRequired: true)
            .WithRequestBody<RegisterEntryRequest>(isRequired: true)
            .Produces<EntryResponse>(StatusCodes.Status201Created)
            .ProducesLedgerProblem(
                StatusCodes.Status400BadRequest,
                ProblemCatalog.ValidationFailed,
                ProblemCatalog.IdempotencyKeyRequired)
            .ProducesLedgerProblem(StatusCodes.Status401Unauthorized, ProblemCatalog.Unauthenticated)
            .ProducesLedgerProblem(StatusCodes.Status403Forbidden, ProblemCatalog.Forbidden)
            .ProducesLedgerProblem(StatusCodes.Status404NotFound, ProblemCatalog.AccountNotFound)
            .ProducesLedgerProblem(StatusCodes.Status413PayloadTooLarge, ProblemCatalog.PayloadTooLarge)
            .ProducesLedgerProblem(StatusCodes.Status415UnsupportedMediaType, ProblemCatalog.UnsupportedMediaType)
            .ProducesLedgerProblem(
                StatusCodes.Status422UnprocessableEntity,
                ProblemCatalog.InsufficientFunds,
                ProblemCatalog.CurrencyMismatch,
                ProblemCatalog.IdempotencyKeyReused)
            .ProducesLedgerProblem(StatusCodes.Status429TooManyRequests, ProblemCatalog.RateLimited)
            .ProducesLedgerProblem(StatusCodes.Status500InternalServerError, ProblemCatalog.InternalError)
            .ProducesLedgerProblem(StatusCodes.Status503ServiceUnavailable, ProblemCatalog.ServiceUnavailable);

        group.MapPost(ReverseRoute, ReverseAsync)
            .RequireAuthorization(AuthorizationPolicies.LedgerWrite)
            .WithRequestClass(RequestClass.Write)
            .WithName("ReverseEntry")
            .WithTags(LedgerDocumentTransformer.EntriesTag)
            .WithSummary("Estorna um lançamento por inteiro com um novo lançamento do tipo oposto.")
            .WithDescription(
                "Exige o cabeçalho Idempotency-Key. O corpo é opcional e aceita apenas description. " +
                "Um lançamento pode ser estornado uma única vez, e um estorno não pode ser estornado.")
            .WithIdempotencyKey(isRequired: true)
            .WithRequestBody<ReverseEntryRequest>(isRequired: false)
            .Produces<EntryResponse>(StatusCodes.Status201Created)
            .ProducesLedgerProblem(
                StatusCodes.Status400BadRequest,
                ProblemCatalog.ValidationFailed,
                ProblemCatalog.IdempotencyKeyRequired)
            .ProducesLedgerProblem(StatusCodes.Status401Unauthorized, ProblemCatalog.Unauthenticated)
            .ProducesLedgerProblem(StatusCodes.Status403Forbidden, ProblemCatalog.Forbidden)
            .ProducesLedgerProblem(
                StatusCodes.Status404NotFound,
                ProblemCatalog.AccountNotFound,
                ProblemCatalog.EntryNotFound)
            .ProducesLedgerProblem(StatusCodes.Status409Conflict, ProblemCatalog.EntryAlreadyReversed)
            .ProducesLedgerProblem(StatusCodes.Status413PayloadTooLarge, ProblemCatalog.PayloadTooLarge)
            .ProducesLedgerProblem(StatusCodes.Status415UnsupportedMediaType, ProblemCatalog.UnsupportedMediaType)
            .ProducesLedgerProblem(
                StatusCodes.Status422UnprocessableEntity,
                ProblemCatalog.EntryNotReversible,
                ProblemCatalog.InsufficientFunds,
                ProblemCatalog.IdempotencyKeyReused)
            .ProducesLedgerProblem(StatusCodes.Status429TooManyRequests, ProblemCatalog.RateLimited)
            .ProducesLedgerProblem(StatusCodes.Status500InternalServerError, ProblemCatalog.InternalError)
            .ProducesLedgerProblem(StatusCodes.Status503ServiceUnavailable, ProblemCatalog.ServiceUnavailable);
    }

    private static async Task<IResult> RegisterAsync(
        string accountId,
        HttpContext context,
        RegisterEntryRequestReader reader,
        RegisterEntryHandler handler,
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

        var account = AccountId.From(accountId);

        if (account.IsFailure)
        {
            return WriteResults.Failure(context, account.Error);
        }

        var input = reader.Read(body.Bytes);
        var key = IdempotencyKeyReader.Read(context.Request.Headers);

        if (!input.IsValid || key.Issue is not null)
        {
            return WriteResults.Invalid(context, WriteResults.Combine(input.Issues, key.Issue));
        }

        if (key.IsMissing)
        {
            return WriteResults.Failure(context, EntryErrors.IdempotencyKeyRequired);
        }

        var command = new RegisterEntryCommand(
            account.Value,
            key.Key,
            input.Value.Type,
            input.Value.Amount,
            input.Value.OccurredAt,
            input.Value.Description,
            input.Value.Reference,
            WriteResults.ClientIdOf(context),
            WriteResults.CorrelationIdOf(context),
            WriteResults.TraceParentOf());

        var result = await handler.HandleAsync(command, cancellationToken);

        return result.IsSuccess
            ? WriteResults.CreatedEntry(context, result.Value)
            : WriteResults.Failure(context, result.Error);
    }

    private static async Task<IResult> ReverseAsync(
        string accountId,
        string entryId,
        HttpContext context,
        ReverseEntryHandler handler,
        CancellationToken cancellationToken)
    {
        var body = await RequestBodyReader.ReadAsync(context.Request, cancellationToken);

        if (body.IsTooLarge)
        {
            return WriteResults.PayloadTooLarge(context);
        }

        if (!body.IsEmpty && !JsonContentType.IsAccepted(context.Request.ContentType))
        {
            return WriteResults.UnsupportedMediaType(context);
        }

        var account = AccountId.From(accountId);

        if (account.IsFailure)
        {
            return WriteResults.Failure(context, account.Error);
        }

        var original = EntryId.From(entryId);

        if (original.IsFailure)
        {
            return WriteResults.Failure(context, original.Error);
        }

        var input = ReverseEntryRequestReader.Read(body.Bytes);
        var key = IdempotencyKeyReader.Read(context.Request.Headers);

        if (!input.IsValid || key.Issue is not null)
        {
            return WriteResults.Invalid(context, WriteResults.Combine(input.Issues, key.Issue));
        }

        if (key.IsMissing)
        {
            return WriteResults.Failure(context, EntryErrors.IdempotencyKeyRequired);
        }

        var command = new ReverseEntryCommand(
            account.Value,
            original.Value,
            key.Key,
            input.Value.Description,
            WriteResults.ClientIdOf(context),
            WriteResults.CorrelationIdOf(context),
            WriteResults.TraceParentOf());

        var result = await handler.HandleAsync(command, cancellationToken);

        return result.IsSuccess
            ? WriteResults.CreatedEntry(context, result.Value)
            : WriteResults.Failure(context, result.Error);
    }
}
