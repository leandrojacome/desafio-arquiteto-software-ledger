using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ledger.Api.OpenApi;

internal sealed class LedgerDocumentTransformer : IOpenApiDocumentTransformer
{
    public const string AccountsTag = "Accounts";

    public const string EntriesTag = "Entries";

    public const string BalancesTag = "Balances";

    private const string Description =
        "API interna do ledger de um banco digital. Registra créditos e débitos nas contas e informa o saldo " +
        "de uma conta em qualquer instante, sem perder nem duplicar dinheiro. Toda rota de negócio exige um " +
        "token bearer com o escopo ledger.read ou ledger.write. As escritas são idempotentes por meio do " +
        "cabeçalho Idempotency-Key. Todo erro é um documento Problem Details com um código estável. " +
        "Os instantes recebidos aceitam deslocamento de fuso ISO 8601, como Z ou -03:00 (horário de Brasília), " +
        "e todo instante devolvido vem em UTC com o sufixo Z.";

    private const string BearerDescription =
        "Token de acesso JWT emitido pelo provedor de identidade do banco com o fluxo client credentials. " +
        "A claim scope traz ledger.read, ledger.write ou ambos, e a claim client_id identifica o chamador.";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info = new OpenApiInfo
        {
            Title = "Ledger API",
            Version = "1.0.0",
            Description = Description
        };

        document.Servers = [];

        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = AccountsTag, Description = "Criação de contas." },
            new() { Name = EntriesTag, Description = "Créditos, débitos, estornos e extrato de uma conta." },
            new() { Name = BalancesTag, Description = "Saldo de uma conta agora ou em um instante do passado." }
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[OpenApiNames.BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = BearerDescription
        };

        return Task.CompletedTask;
    }
}
