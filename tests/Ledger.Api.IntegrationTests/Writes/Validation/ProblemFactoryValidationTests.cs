using Ledger.Api.ErrorHandling;
using Ledger.Api.Validation;
using Microsoft.AspNetCore.Http;

namespace Ledger.Api.IntegrationTests.Writes.Validation;

[Trait("Category", "Unit")]
public sealed class ProblemFactoryValidationTests
{
    [Fact]
    public void FromValidation_BuildsTheCatalogProblemWithTheListOfIssues()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/accounts/x/entries";
        context.Items["Ledger.CorrelationId"] = "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10";
        ValidationIssue[] issues =
        [
            new("amount", "TOO_MANY_DECIMALS", "O campo 'amount' deve ter no máximo 2 casas decimais."),
            new("occurredAt", "IN_THE_FUTURE", "O campo 'occurredAt' não pode estar mais de 5 minutos à frente do relógio do servidor.")
        ];

        var problem = ProblemFactory.FromValidation(context, issues);

        problem.Status.ShouldBe(400);
        problem.Title.ShouldBe("Falha na validação da requisição");
        problem.Type.ShouldBe("https://ledger.bank.internal/problems/validation-failed");
        problem.Detail.ShouldBe("Um ou mais campos são inválidos.");
        problem.Instance.ShouldBe("/v1/accounts/x/entries");
        problem.Extensions["code"].ShouldBe("VALIDATION_FAILED");
        problem.Extensions["correlationId"].ShouldBe("9f3c1a7e2b4d4f60a1c8e5d7b3a29f10");
        problem.Extensions["traceId"].ShouldNotBeNull();
        problem.Extensions["errors"].ShouldBeSameAs(issues);
    }
}
