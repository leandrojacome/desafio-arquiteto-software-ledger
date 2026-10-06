using Ledger.Api.ErrorHandling;
using Ledger.Api.Middleware;
using Ledger.Api.Observability;
using Ledger.Api.Security;
using Serilog;

namespace Ledger.Api;

internal static class ApplicationBuilderExtensions
{
    private const string RequestLogTemplate =
        "HTTP {RequestMethod:l} {RequestRoute:l} responded {StatusCode} in {Elapsed:0.0} ms";

    public static IApplicationBuilder UseLedgerPipeline(this IApplicationBuilder app)
    {
        if (ForwardedHeadersSetup.IsEnabled(app.ApplicationServices))
        {
            app.UseForwardedHeaders();
        }

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<ProblemMediaTypeMiddleware>();

        var requestLogLevel = new ColdStartRequestLogLevel();

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = RequestLogTemplate;
            options.GetLevel = requestLogLevel.For;
            options.EnrichDiagnosticContext = RequestLogEnricher.Enrich;
        });

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRequestTimeouts();
        app.UseMiddleware<RequestTimeoutMarkerMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<ClientIdActivityMiddleware>();
        app.UseRateLimiter();
        app.UseAuthorization();

        return app;
    }
}
