# GitHub Actions

Um workflow em `.github/workflows/ci.yml` mostra o resultado da validação do ledger na aba Actions do repositório ([documento de arquitetura 0016](../03-principios-e-decisoes/documento-arquitetura/0016-azure-devops-como-plataforma-de-ci-cd.md)). Ele roda a cada push no `main`, a cada pull request para o `main` e sob demanda (`workflow_dispatch`), em máquina `ubuntu-24.04`, com o SDK do .NET 10 (`10.0.x`) e cache de pacotes NuGet. O token é só de leitura (`permissions: contents: read`), o arquivo não lê segredo nem usa `pull_request_target`, e uma execução nova num pull request cancela a anterior do mesmo ramo.

## O que ele roda

| Job | Comandos | Espera | Prazo máximo |
|---|---|---|---|
| `validate` | `dotnet build -c Release`, `dotnet format --verify-no-changes` e `dotnet list package --vulnerable --include-transitive` com `DOTNET_CLI_UI_LANGUAGE=en`, que reprova se o relatório trouxer `has the following vulnerable packages` | Nenhum | 25 min |
| `unit` | `dotnet test` da solução com o filtro `FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~EndToEnd`, depois os pisos de cobertura do domínio (95% de linhas e 90% de ramos) e da aplicação (90% e 80%) | `validate` | 20 min |
| `integration` | `dotnet test tests/Ledger.Api.IntegrationTests` com PostgreSQL e RabbitMQ reais: com `--filter "Speed!=Slow"` em pull request, e sem filtro nas demais execuções | `validate` | 60 min |

Cada job começa com um checkout raso (`fetch-depth: 1`) e instala o SDK e o cache de pacotes com `actions/checkout@v7`, `actions/setup-dotnet@v6` e `actions/cache@v6`. Os resultados e a cobertura sobem com `actions/upload-artifact@v7`, mesmo quando um teste falha, nos artefatos `resultados-unidade` e `resultados-integracao`, que ficam 14 dias. O ambiente define `LEDGER_REQUIRE_DOCKER=true`, então um teste que dependa de Docker falha em vez de ser ignorado ([documento de arquitetura 0022](../03-principios-e-decisoes/documento-arquitetura/0022-testes-dependentes-de-ambiente-falham-por-padrao.md)).

## O que fica só no Azure DevOps

A varredura de segredos, o empacotamento e a varredura das imagens, o teste fim a fim, a publicação do documento OpenAPI como artefato e o envio ao registro. Cada estágio está em [Pipeline no Azure DevOps](pipeline-azure-devops.md). O teste de contrato do OpenAPI roda aqui dentro da integração, porque faz parte do projeto `Ledger.Api.IntegrationTests`.

## Como o arquivo é conferido

Cada passo chama um comando direto, os mesmos que o pipeline do Azure DevOps chama e que rodam na máquina de quem desenvolve ([Execução local](execucao-local.md)). O `GitHubWorkflowTests`, no projeto de arquitetura, confere o contrato do arquivo: os três jobs e suas dependências, os gatilhos, a permissão de leitura, a ausência de segredo e de `pull_request_target`, o prazo de cada job, o checkout raso, as ações fixadas pela versão principal e o envio dos resultados com `!cancelled()`. Ele também falha se o workflow rodar um comando que o pipeline do Azure DevOps não roda. Para rodar só esses testes:

```sh
dotnet test tests/Ledger.Architecture.Tests --filter 'FullyQualifiedName~GitHubWorkflowTests'
```

O workflow nunca executou no GitHub. [Limites conhecidos](../09-qualidade/limites-conhecidos.md) diz o que isso deixa sem exercitar, a começar pelo tempo e pela memória do runner hospedado com os testes de integração no perfil completo.
