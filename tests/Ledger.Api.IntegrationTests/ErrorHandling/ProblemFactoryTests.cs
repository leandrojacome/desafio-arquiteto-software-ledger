using Ledger.Api.ErrorHandling;
using Ledger.Domain.Shared;
using Microsoft.AspNetCore.Http;

namespace Ledger.Api.IntegrationTests.ErrorHandling;

[Trait("Category", "Unit")]
public sealed class ProblemFactoryTests
{
    [Theory]
    [InlineData(ErrorKind.Validation, 400)]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Conflict, 409)]
    [InlineData(ErrorKind.Unprocessable, 422)]
    public void AnErrorKind_MapsToItsHttpStatus(ErrorKind kind, int expected)
    {
        var problem = ProblemFactory.FromError(new DefaultHttpContext(), new Error("VALIDATION_FAILED", "O motivo.", kind));

        problem.Status.ShouldBe(expected);
    }

    [Fact]
    public void AnErrorKindOutsideTheKnownOnes_IsABug()
    {
        var unknown = new Error("VALIDATION_FAILED", "O motivo.", (ErrorKind)99);

        Should.Throw<ArgumentOutOfRangeException>(() => ProblemFactory.FromError(new DefaultHttpContext(), unknown));
    }

    [Fact]
    public void FromError_CarriesTheCodeTheMessageAndTheCatalogTexts()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/accounts/x/entries";
        var error = new Error("INSUFFICIENT_FUNDS", "O saldo não cobre o valor.", ErrorKind.Unprocessable);

        var problem = ProblemFactory.FromError(context, error);

        problem.Extensions["code"].ShouldBe("INSUFFICIENT_FUNDS");
        problem.Detail.ShouldBe("O saldo não cobre o valor.");
        problem.Title.ShouldBe("Saldo insuficiente");
        problem.Type.ShouldBe("https://ledger.bank.internal/problems/insufficient-funds");
        problem.Instance.ShouldBe("/v1/accounts/x/entries");
        problem.Extensions.ContainsKey("correlationId").ShouldBeTrue();
        problem.Extensions.ContainsKey("traceId").ShouldBeTrue();
    }

    [Fact]
    public void AnErrorCodeOutsideTheCatalog_BecomesAnInternalErrorWithoutLeakingItsMessage()
    {
        var error = new Error("NOT_IN_THE_CATALOG", "texto interno que não pode sair", ErrorKind.Conflict);

        var problem = ProblemFactory.FromError(new DefaultHttpContext(), error);

        problem.Status.ShouldBe(StatusCodes.Status500InternalServerError);
        problem.Extensions["code"].ShouldBe("INTERNAL_ERROR");
        problem.Detail.ShouldBe(ProblemCatalog.DetailFor("INTERNAL_ERROR"));
        (problem.Detail ?? string.Empty).ShouldNotContain("texto interno");
    }

    [Theory]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status429TooManyRequests)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    public void Create_WithoutADetail_FillsTheCatalogDetail(int status)
    {
        var code = ProblemCatalog.CodeFor(status);

        var problem = ProblemFactory.Create(new DefaultHttpContext(), status, code);

        problem.Detail.ShouldBe(ProblemCatalog.DetailFor(code));
        problem.Detail.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TheQueryString_NeverReachesTheInstance()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/accounts/x/balance";
        context.Request.QueryString = new QueryString("?asOf=secret");

        var problem = ProblemFactory.Create(context, 400, "VALIDATION_FAILED");

        problem.Instance.ShouldBe("/v1/accounts/x/balance");
    }
}
