# Garantias automáticas

Parte do que o projeto promete não depende de alguém lembrar de conferir: o build e os testes de arquitetura reprovam a mudança que quebra a promessa. A página lista o que cada um garante e como rodá-lo, e os dois pedem só o SDK do .NET 10. Os mesmos comandos rodam na máquina de quem altera o código e nos pipelines ([Azure DevOps](../10-implantacao-e-entrega/pipeline-azure-devops.md) e [GitHub Actions](../10-implantacao-e-entrega/github-actions.md)). As regras de código que o build impõe estão em [Convenções de código](../12-engenharia/convencoes-de-codigo.md), e os testes de comportamento, em [Estratégia de testes](estrategia-de-testes.md).

## O que o build garante

`dotnet build -c Release` compila a solução inteira, testes incluídos. O `Directory.Build.props` liga `TreatWarningsAsErrors`, `Nullable`, `AnalysisLevel` em `latest-all` com `AnalysisMode` em `All` e `EnforceCodeStyleInBuild`, então aviso do compilador, aviso de analisador e regra de estilo do `.editorconfig` violada quebram a compilação. A exceção pontual é um `SuppressMessage` com `Justification`, e `#pragma warning disable` não entra ([documento de arquitetura 0015](../03-principios-e-decisoes/documento-arquitetura/0015-sem-comentarios-e-zero-warnings.md)). A formatação se confere à parte, com `dotnet format --verify-no-changes`, que reprova arquivo fora do `.editorconfig`.

O `Microsoft.CodeAnalysis.BannedApiAnalyzers` lê o `BannedSymbols.txt` e proíbe o que costuma dar defeito num ledger: o relógio do sistema (`DateTime.Now`, `UtcNow`, `Today` e os equivalentes de `DateTimeOffset`) fora do `TimeProvider`, `Guid.NewGuid` fora do `IIdGenerator`, `Thread.Sleep`, o bloqueio sobre `Task` (`Wait`, `.Result`, `GetResult`) e `ConfigureAwait`, que no ASP.NET Core não faz nada. Os projetos de teste usam o `tests/BannedSymbols.txt`, a mesma lista sem a regra do `Guid.NewGuid`. O `BannedSymbolsTests` confere que as duas listas não se afastam e que nenhum arquivo de `src/` chama `Guid.NewGuid`.

O `Directory.Build.props` liga também a auditoria do NuGet para pacote direto e transitivo (`NuGetAuditMode` em `all`, `NuGetAuditLevel` em `low`), e como os avisos são erros, vulnerabilidade conhecida quebra o build. O comando explícito, que os dois pipelines também rodam, é:

```bash
dotnet list package --vulnerable --include-transitive
```

Os pipelines o reprovam quando a saída traz a frase `has the following vulnerable packages`. Um resultado limpo só vale se a saída não trouxer `NU1900`. O NuGet emite esse aviso quando não alcança o serviço de vulnerabilidades do `api.nuget.org`, o `Directory.Build.props` o tira da lista de erros (`WarningsNotAsErrors`) e o comando termina sem a frase procurada, de modo que uma máquina sem rede passa sem ter avaliado nada.

Os pisos de cobertura do domínio e da aplicação são impostos pelo `coverlet.msbuild`, que faz o próprio comando de teste falhar ([Cobertura](estrategia-de-testes.md#cobertura)).

## O que os testes de arquitetura garantem

O `Ledger.Architecture.Tests` não precisa de Docker (`dotnet test tests/Ledger.Architecture.Tests`). Ele usa o NetArchTest para as regras de dependência e lê os arquivos do repositório para as demais. O NetArchTest não olha o corpo dos métodos, e por isso o que depende do corpo, como o `DateTime.UtcNow`, fica com os símbolos proibidos do build.

| Garantia | Teste | Reprova quando |
|---|---|---|
| Direção das camadas | `LayerDependencyTests` | O domínio depende de outro assembly do projeto ou de biblioteca de dados, mensageria ou web. A aplicação depende de algo além do domínio. A infraestrutura depende da API ou do Worker, ou a API e o Worker dependem um do outro. Um endpoint referencia acesso a dados ou a infraestrutura. Algo fora dos tipos de persistência usa `Npgsql` ou Dapper |
| Forma dos casos de uso | `LayerDependencyTests` | Um `Handler` não é selado, não expõe um único método público `HandleAsync` ou recebe o `CancellationToken` fora da última posição. Uma interface de porta tem implementação fora da infraestrutura |
| Superfície da infraestrutura | `InfrastructurePublicSurfaceTests` | A infraestrutura expõe tipo que os executáveis não precisam, ou deixa pública uma classe de persistência, mensageria ou segurança |
| Projetos de teste | `TestProjectDependencyTests` | Um projeto de unidade referencia camada acima da que testa |
| Sem diretiva nem supressão de nulabilidade | `RoslynSyntaxGuardTests` | Há `#pragma` em `src/` ou `tests/`, ou o operador `!` de supressão de nulabilidade em `src/`. Cada regra tem um caso plantado no próprio teste |
| Ledger imutável | `LedgerEntriesAreNeverMutatedTests` | O SQL de produção tem `UPDATE`, `DELETE` ou `TRUNCATE` em `ledger_entries` ou `audit_log`, ou há `UPDATE` ou `DELETE` no outbox e nas contas além dos que o desenho prevê (reivindicar, marcar e liberar mensagens, podá-las, e a rotação das chaves do documento) |
| Cifra num lugar só | `CryptographyBoundaryTests` | Primitiva de cifra, de MAC ou de aleatoriedade de chave aparece fora da pasta `Security` da infraestrutura |
| Migração aplicada nunca é editada | `MigrationChecksumTests` | A soma SHA-256 de um script difere da de `migrations.sha256`, falta linha no arquivo ou sobra linha |
| Toda opção é lida | `ConfigurationIsReadTests` | Uma propriedade de opção é declarada e nunca lida pelo código |
| Toda chave de configuração é ligada | `AppSettingsKeysAreBoundTests` | Uma chave dos `appsettings.json` não é lida por nenhuma classe de opções, de modo que editá-la não mudaria nada |
| Porta dos contêineres | `DockerfileListeningPortTests` | Uma imagem escolhe a porta por outro meio que `ASPNETCORE_HTTP_PORTS` ou define `ASPNETCORE_URLS` |

## O que liga a documentação ao sistema

Parte do que as páginas afirmam é conferida por teste, de modo que código e página não se afastam sem quebrar a suíte.

| O que confere | Teste | Reprova quando |
|---|---|---|
| O documento OpenAPI versionado é o que a API gera | `OpenApiContractTests` | O documento gerado difere de `docs/05-contratos/openapi.v1.json`. Quando o contrato muda de propósito, o arquivo se regenera com `LEDGER_OPENAPI_UPDATE=true dotnet test tests/Ledger.Api.IntegrationTests --filter "FullyQualifiedName~OpenApiContractTests"` |
| A rota do documento só existe em desenvolvimento e teste | `OpenApiExposureTests` | A rota responde em outro ambiente |
| Cada instrumento do código tem linha no catálogo, e cada linha tem instrumento | `InstrumentCatalogTests` | Há instrumento sem linha ou linha sem instrumento em [Catálogo de métricas](../08-resiliencia-e-operacao/catalogo-de-metricas.md) |
| Cada instrução SQL que as páginas de [fluxos](../06-fluxos/README.md) reproduzem é igual, com os espaços normalizados, à constante do código | `CriticalSqlMatchesFlowPagesTests` e `ReadSqlMatchesFlowPagesTests` | O SQL do código muda sem a página, ou a página muda sem o SQL |
| Cada requisito funcional tem testes, e cada teste citado existe | `RequirementsMatrixTests` | Um requisito fica sem linha em [Rastreabilidade de requisitos](../02-contexto-e-requisitos/rastreabilidade.md), uma linha cita requisito inexistente, ou uma página cita classe `*Tests` que não existe em `tests/` |
| Os links e as imagens relativos dos Markdown resolvem | `DocumentationLinksTests` | Um link ou imagem aponta para arquivo que não existe, ou a âncora aponta para um título que não existe no arquivo de destino |
| A estrutura do pipeline do Azure DevOps é a esperada | `CiPipelineTests` | Estágio, job, dependência ou prazo máximo diverge do esperado, uma tarefa ou imagem de ferramenta fica sem versão fixada, ou um passo fora do estágio de entrega faz login ou envia imagem a um registro |
| O workflow do GitHub Actions é enxuto e só chama o que o pipeline do Azure DevOps chama | `GitHubWorkflowTests` | Job, dependência, tempo limite ou permissão diverge do esperado, uma ação fica sem versão principal fixada, o workflow usa `pull_request_target` ou segredo, ou roda um comando que o pipeline do Azure DevOps não roda |

O `DocumentationLinksTests` percorre os Markdown do repositório, sem as pastas ocultas e as de saída de build, e não confere link externo, trecho de código nem bloco cercado. O `OpenApiContractTests` e o `OpenApiExposureTests` moram na integração, mas não usam Docker. O `InstrumentCatalogTests` roda com `dotnet test tests/Ledger.Infrastructure.Tests --filter "FullyQualifiedName~InstrumentCatalogTests"`.

## O que só roda no pipeline do Azure DevOps

Duas verificações usam contêineres e não existem no workflow do GitHub Actions. A varredura de segredos executa o gitleaks sobre a árvore de arquivos, sem histórico (`detect --no-git --source /repo --redact --exit-code 1`). O `.gitleaks.toml` estende as regras padrão e libera, por valor exato, os vetores de teste e de exemplo e, por caminho, o `.env.example`. Como a lista casa o valor inteiro e não o arquivo, uma chave de verdade colocada no mesmo arquivo continua sendo pega, e um vetor novo reprova a varredura até o valor entrar na lista.

O estágio `package` constrói as imagens `ledger-api` e `ledger-worker` e as varre com o Trivy (`--severity HIGH,CRITICAL --exit-code 1 --ignore-unfixed`). Uma vulnerabilidade com correção numa imagem base reprova o estágio mesmo sem mudança no código, e a saída é atualizar o resumo da imagem base no Dockerfile. O estágio publica também a lista de componentes de cada imagem em CycloneDX. O gitleaks, o Trivy, as imagens base dos Dockerfiles e a imagem do broker no Compose são fixados por versão e por resumo criptográfico, o que o `CiPipelineTests` confere.

Nesse pipeline, o estágio `validate` roda a compilação, a formatação e a busca de pacotes vulneráveis, e a varredura de segredos em paralelo. Os estágios `test` e `package` só começam depois dele, para o que se resolve em segundos não esperar os testes de minutos. A relação completa de jobs e comandos está em [Pipeline no Azure DevOps](../10-implantacao-e-entrega/pipeline-azure-devops.md).
