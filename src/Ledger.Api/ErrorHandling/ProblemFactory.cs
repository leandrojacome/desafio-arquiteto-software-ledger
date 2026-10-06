using System.Diagnostics;
using Ledger.Api.Middleware;
using Ledger.Api.Validation;
using Ledger.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Api.ErrorHandling;

internal static class ProblemFactory
{
    private const string ErrorsExtension = "errors";

    public static ProblemDetails Create(HttpContext context, int status, string code, string? detail = null)
    {
        var problem = new ProblemDetails
        {
            Type = ProblemCatalog.TypeFor(code),
            Title = ProblemCatalog.TitleFor(code),
            Status = status,
            Detail = detail ?? ProblemCatalog.DetailFor(code),
            Instance = context.Request.Path.Value
        };

        Decorate(context, problem, code);

        return problem;
    }

    public static ProblemDetails FromError(HttpContext context, Error error)
    {
        return ProblemCatalog.Contains(error.Code)
            ? Create(context, StatusFor(error.Kind), error.Code, error.Message)
            : Create(context, StatusCodes.Status500InternalServerError, ProblemCatalog.InternalError);
    }

    public static ProblemDetails FromValidation(HttpContext context, IReadOnlyList<ValidationIssue> issues)
    {
        var problem = Create(context, StatusCodes.Status400BadRequest, ProblemCatalog.ValidationFailed);
        problem.Extensions[ErrorsExtension] = issues;

        return problem;
    }

    private static int StatusFor(ErrorKind kind)
    {
        return kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown error kind.")
        };
    }

    public static void Decorate(HttpContext context, ProblemDetails problem, string code)
    {
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = CorrelationIdMiddleware.Resolve(context);
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    }
}
