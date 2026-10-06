using System.Globalization;
using System.Text.Json.Nodes;
using Ledger.Api.Contracts;
using Ledger.Api.Security;
using Ledger.Api.Validation;
using Ledger.Domain.Entries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ledger.Api.OpenApi;

internal sealed class LedgerOperationTransformer : IOpenApiOperationTransformer
{
    private const string CorrelationIdPattern = "^[A-Za-z0-9._:-]{8,64}$";
    private const int CorrelationIdMinLength = 8;
    private const int CorrelationIdMaxLength = 64;
    private const string TooManyRequests = "429";
    private const string ServiceUnavailable = "503";
    private const string Unauthorized = "401";
    private const string Created = "201";
    private const string Success = "200";

    private static readonly Dictionary<string, string> PathParameterDescriptions = new(StringComparer.Ordinal)
    {
        ["accountId"] = "Identificador da conta, na forma canônica em minúsculas e com hifens. Qualquer outro texto responde ACCOUNT_NOT_FOUND.",
        ["entryId"] = "Identificador do lançamento a estornar, na forma canônica em minúsculas e com hifens. Qualquer outro texto responde ENTRY_NOT_FOUND."
    };

    private const string IdempotencyKeyDescription =
        "Define a identidade da escrita. Repetir a requisição com a mesma chave e o mesmo conteúdo devolve o " +
        "resultado original com Idempotent-Replayed: true. A mesma chave com conteúdo diferente é recusada com " +
        "422 IDEMPOTENCY_KEY_REUSED. Até 128 caracteres ASCII visíveis.";

    private const string CorrelationIdDescription =
        "Identificador que acompanha a requisição nos logs. Um valor válido é mantido, e, na falta dele, o " +
        "serviço gera um. Ele sempre volta na resposta.";

    private const string SuccessDescription = "Sucesso.";
    private const string CreatedDescription = "Criado.";

    private static readonly Dictionary<int, string> StatusPhrases = new()
    {
        [StatusCodes.Status400BadRequest] = "Requisição inválida",
        [StatusCodes.Status401Unauthorized] = "Não autenticado",
        [StatusCodes.Status403Forbidden] = "Acesso negado",
        [StatusCodes.Status404NotFound] = "Não encontrado",
        [StatusCodes.Status409Conflict] = "Conflito",
        [StatusCodes.Status413PayloadTooLarge] = "Corpo da requisição grande demais",
        [StatusCodes.Status415UnsupportedMediaType] = "Tipo de mídia não suportado",
        [StatusCodes.Status422UnprocessableEntity] = "Requisição não processável",
        [StatusCodes.Status429TooManyRequests] = "Excesso de requisições",
        [StatusCodes.Status500InternalServerError] = "Erro interno do servidor",
        [StatusCodes.Status503ServiceUnavailable] = "Serviço indisponível"
    };

    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        DescribeAuthorization(operation, context, metadata);
        await DescribeRequestBodyAsync(operation, context, metadata, cancellationToken);
        DescribePathParameters(operation);
        DescribeRequestHeaders(operation, metadata);
        DescribeQueryParameters(operation, metadata);
        DescribeResponses(operation, metadata);
    }

    private static async Task DescribeRequestBodyAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        IList<object> metadata,
        CancellationToken cancellationToken)
    {
        if (metadata.OfType<RequestBodyMetadata>().FirstOrDefault() is not { } body)
        {
            return;
        }

        var schema = await context.GetOrCreateSchemaAsync(body.ContentType, null, cancellationToken);

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = body.IsRequired,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                [OpenApiNames.JsonMediaType] = new OpenApiMediaType { Schema = schema }
            }
        };
    }

    private static void DescribeAuthorization(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        IList<object> metadata)
    {
        var policies = metadata
            .OfType<IAuthorizeData>()
            .Select(data => data.Policy)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (policies.Count == 0 || metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(OpenApiNames.BearerScheme, context.Document)] = []
        });

        operation.Description = string.Concat(operation.Description, "\n\n", ScopeSentence(policies));
    }

    private static string ScopeSentence(List<string> policies)
    {
        if (policies.Contains(AuthorizationPolicies.AccountProvisioning, StringComparer.Ordinal))
        {
            return "Exige o escopo ledger.write e um chamador autorizado a provisionar contas.";
        }

        return policies.Count == 1
            ? $"Exige o escopo {policies[0]}."
            : $"Exige os escopos {string.Join(" e ", policies)}.";
    }

    private static void DescribePathParameters(OpenApiOperation operation)
    {
        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            if (parameter.In == ParameterLocation.Path && PathParameterDescriptions.TryGetValue(parameter.Name ?? string.Empty, out var text))
            {
                parameter.Description = text;
            }
        }
    }

    private static void DescribeRequestHeaders(OpenApiOperation operation, IList<object> metadata)
    {
        if (!metadata.OfType<IAuthorizeData>().Any())
        {
            return;
        }

        operation.Parameters ??= [];

        if (metadata.OfType<IdempotencyKeyMetadata>().FirstOrDefault() is { } key)
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = IdempotencyKeyReader.HeaderName,
                In = ParameterLocation.Header,
                Required = key.IsRequired,
                Description = IdempotencyKeyDescription,
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    MinLength = 1,
                    MaxLength = IdempotencyKey.MaxLength,
                    Pattern = ContractPatterns.VisibleAscii
                }
            });
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = ApiConstants.CorrelationIdHeader,
            In = ParameterLocation.Header,
            Required = false,
            Description = CorrelationIdDescription,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MinLength = CorrelationIdMinLength,
                MaxLength = CorrelationIdMaxLength,
                Pattern = CorrelationIdPattern
            }
        });
    }

    private static void DescribeQueryParameters(OpenApiOperation operation, IList<object> metadata)
    {
        foreach (var parameter in metadata.OfType<QueryParameterMetadata>())
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = parameter.Name,
                In = ParameterLocation.Query,
                Required = false,
                Description = parameter.Description,
                Schema = SchemaOf(parameter)
            });
        }
    }

    private static OpenApiSchema SchemaOf(QueryParameterMetadata parameter)
    {
        var schema = new OpenApiSchema();

        switch (parameter.Kind)
        {
            case QueryParameterKind.Instant:
                schema.Type = JsonSchemaType.String;
                schema.Format = "date-time";
                break;
            case QueryParameterKind.Integer:
                schema.Type = JsonSchemaType.Integer;
                schema.Minimum = parameter.Minimum?.ToString(CultureInfo.InvariantCulture);
                schema.Maximum = parameter.Maximum?.ToString(CultureInfo.InvariantCulture);
                schema.Default = parameter.DefaultValue is { } value ? JsonValue.Create(value) : null;
                break;
            default:
                schema.Type = JsonSchemaType.String;
                schema.MaxLength = parameter.MaxLength;
                break;
        }

        return schema;
    }

    private static void DescribeResponses(OpenApiOperation operation, IList<object> metadata)
    {
        if (operation.Responses is null)
        {
            return;
        }

        var codes = metadata.OfType<ProblemCodesMetadata>().ToDictionary(item => item.Status);
        var isIdempotent = metadata.OfType<IdempotencyKeyMetadata>().Any();

        foreach (var (key, response) in operation.Responses)
        {
            if (response is not OpenApiResponse concrete)
            {
                continue;
            }

            if (int.TryParse(key, CultureInfo.InvariantCulture, out var status) && codes.TryGetValue(status, out var problem))
            {
                concrete.Description = DescribeProblem(status, problem.Codes);
            }
            else if (key == Success)
            {
                concrete.Description = SuccessDescription;
            }
            else if (key == Created)
            {
                concrete.Description = CreatedDescription;
            }

            concrete.Headers ??= new Dictionary<string, IOpenApiHeader>();
            concrete.Headers[ApiConstants.CorrelationIdHeader] = Header(
                "Identificador de correlação da requisição.",
                JsonSchemaType.String);

            AddStatusHeaders(concrete, key, isIdempotent);
        }
    }

    private static string DescribeProblem(int status, IReadOnlyList<string> codes)
    {
        var label = codes.Count == 1 ? "Código do problema" : "Códigos do problema";
        var phrase = StatusPhrases.TryGetValue(status, out var known)
            ? known
            : throw new InvalidOperationException("The status has no phrase in the OpenAPI document.");

        return $"{phrase}. {label}: {string.Join(", ", codes)}.";
    }

    private static void AddStatusHeaders(OpenApiResponse response, string status, bool isIdempotent)
    {
        var headers = response.Headers ?? throw new InvalidOperationException("The response headers were not created.");

        if (status is TooManyRequests or ServiceUnavailable)
        {
            headers[OpenApiNames.RetryAfterHeader] = Header(
                "Segundos de espera antes de repetir a requisição.",
                JsonSchemaType.Integer);
        }

        if (status == Unauthorized)
        {
            headers[OpenApiNames.WwwAuthenticateHeader] = Header(
                "Desafio Bearer. Nunca informa o motivo da recusa do token.",
                JsonSchemaType.String);
        }

        if (status != Created)
        {
            return;
        }

        headers[OpenApiNames.LocationHeader] = Header("Caminho em que o recurso criado pode ser lido.", JsonSchemaType.String);

        if (isIdempotent)
        {
            headers[ApiConstants.IdempotentReplayedHeader] = new OpenApiHeader
            {
                Description = "Presente, com o valor true, quando a resposta repete o resultado de uma requisição anterior com a mesma Idempotency-Key.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [JsonValue.Create("true")] }
            };
        }
    }

    private static OpenApiHeader Header(string description, JsonSchemaType type) =>
        new() { Description = description, Schema = new OpenApiSchema { Type = type } };
}
