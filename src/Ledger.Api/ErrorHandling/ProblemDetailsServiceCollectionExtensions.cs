namespace Ledger.Api.ErrorHandling;

internal static class ProblemDetailsServiceCollectionExtensions
{
    private const string CodeExtension = "code";

    public static IServiceCollection AddLedgerProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var problem = context.ProblemDetails;
                var status = problem.Status ?? context.HttpContext.Response.StatusCode;
                var code = problem.Extensions.TryGetValue(CodeExtension, out var existing) && existing is string text
                    ? text
                    : ProblemCatalog.CodeFor(status);

                problem.Status = status;
                problem.Type = ProblemCatalog.TypeFor(code);
                problem.Title = ProblemCatalog.TitleFor(code);
                problem.Detail ??= ProblemCatalog.DetailFor(code);
                problem.Instance ??= context.HttpContext.Request.Path.Value;

                ProblemFactory.Decorate(context.HttpContext, problem, code);
            };
        });

        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }
}
