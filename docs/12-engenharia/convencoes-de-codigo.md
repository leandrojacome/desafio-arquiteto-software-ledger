# Convenções de código

Esta página reúne as convenções que o código do ledger segue: nomes, estrutura em camadas, tratamento de erro, assincronia, configuração, dados, logs e testes. O porquê de cada uma está em [Princípios de arquitetura](../03-principios-e-decisoes/principios-de-arquitetura.md), e as decisões com as alternativas descartadas estão no [registro de documentos de arquitetura](../03-principios-e-decisoes/documento-arquitetura/README.md). Boa parte delas vira erro de compilação ou falha de teste, e o texto diz qual.

## Idioma e nomenclatura

Código, tabelas, colunas, rotas e propriedades JSON são em inglês, e a documentação em português do Brasil. O [Glossário](../01-visao-geral/glossario.md) fixa a correspondência: o domínio fala em `Entry`, e não em `Transaction`, e a coluna é `balance_after`, e não `running_balance`.

A fronteira entre os idiomas é quem lê. O que uma pessoa lê do outro lado da API é português: o `title` e o `detail` do Problem Details, a `message` de cada erro por campo, o `Error.Message` de Domain e Application quando chega ao chamador (porque vira o `detail`) e as descrições do documento OpenAPI. O que um programa ou o operador lê fica em inglês: identificadores, códigos de erro (`INSUFFICIENT_FUNDS`), razões por campo (`TOO_MANY_DECIMALS`), nomes de campo, parâmetro, cabeçalho e rota, logs, exceções de programação, falhas de validação de configuração, rótulos de métrica, nomes de evento e tokens de protocolo, como o `error="invalid_token"` do `WWW-Authenticate`. Nomes e mensagens de teste também ([documento de arquitetura 0037](../03-principios-e-decisoes/documento-arquitetura/0037-idioma-das-mensagens-ao-chamador.md)).

A mensagem para o chamador segue estas regras:

- Frase completa, com inicial maiúscula e ponto final, em tom neutro, impessoal ou no imperativo ("Informe o fuso horário."). O `title` é um sintagma curto, sem ponto. Nada de "você", gíria, "por favor", exclamação, reticências, emoji nem travessão, e com a acentuação toda no lugar.
- Os termos do [Glossário](../01-visao-geral/glossario.md): lançamento, estorno, saldo, conta, chave de idempotência, limite de cheque especial, titular, extrato, instante e fuso horário. Nunca "transação", "usuário", "pedido" ou "data/hora".
- O nome de campo, parâmetro ou cabeçalho entra entre aspas simples, na grafia do contrato (`'amount'`). Aspas duplas e chaves não aparecem no texto final.
- A mensagem nunca repete o valor recebido nem dado do titular, e um nome de campo desconhecido só aparece se casar com `^[A-Za-z0-9_]{1,64}$`.
- O número de um limite vem da constante, não é digitado de novo no texto, e o singular vale para 1 (`1 caractere`, `1 minuto`). A classe `AccountErrors` não tem dígito, e as mensagens de uma mesma classe `*Errors` são diferentes entre si.
- Para um instante, a mensagem diz que o fuso horário é obrigatório e dá `'Z'` e `'-03:00'` como exemplo.

O `MessageStyle` dos testes de domínio confere a forma (inicial maiúscula, ponto final, sem travessão, sem palavra em inglês nem sem acento), e `AccountErrorsTests`, `EntryErrorsTests`, `BalanceErrorsTests`, `MoneyErrorsTests`, `StatementErrorsTests` e `ProblemCatalogTests` conferem o resto. O texto é fixo no código, sem catálogo de recursos, e por isso a regra CA1303 fica desligada.

Exemplos de código, de teste e de documentação usam reais: `BRL`, `"amount": "80.00"` no contrato e "R$ 80,00" na prosa, CPF e CNPJ de dígitos verificadores válidos e texto livre em português, como `Pix enviado`. Quando um teste precisa de uma segunda moeda, para provar moeda não suportada ou divergente, o exemplo é `EUR`.

### Instantes e fusos horários

Dentro do sistema todo instante é UTC: armazenamento, cálculo, hash canônico, cursor, eventos e saída da API. A entrada aceita um deslocamento ISO 8601 (`Z`, `+00:00` ou `-03:00`, o horário de Brasília) em `occurredAt`, `asOf`, `from` e `to`, e o leitor da API converte para UTC antes de qualquer outro uso. Instante sem deslocamento é recusado com uma mensagem que pede o fuso. O código usa `DateTimeOffset`, lê o relógio só pelo `TimeProvider` e não tem tabela de fusos nem biblioteca de fuso no domínio, porque o deslocamento é um número que o chamador escreve. As regras e os exemplos estão em [Contrato da API REST](../05-contratos/api-rest.md), e a decisão no [documento de arquitetura 0036](../03-principios-e-decisoes/documento-arquitetura/0036-politica-de-fusos-horarios.md).

### Nomes

Tipos, métodos, propriedades, constantes e membros de enumeração usam PascalCase, e parâmetros e variáveis locais, camelCase. Campo privado só existe quando o construtor primário não resolve e leva o prefixo `_`. Interface começa com `I`, e método assíncrono termina em `Async`. Cada arquivo tem um tipo de nível superior com o nome do arquivo, e o namespace, no formato de arquivo (`namespace Ledger.Domain.Entries;`), acompanha o caminho da pasta. As únicas abreviações são `Id`, `Utc`, `Pii` e `Api`. O `.editorconfig` da raiz transforma nomenclatura, namespace e posição dos `using` em aviso de compilação, e o aviso é erro.

Portas levam o nome do que fazem para o caso de uso, nunca da tecnologia: `IEntryRepository` é a interface, e `PostgresEntryRepository` a implementação que conhece o banco. Casos de uso são um par `<Verbo><Objeto>Command` (ou `Query`) e `<Verbo><Objeto>Handler`, com um único método público, `HandleAsync`: `RegisterEntryCommand` e `RegisterEntryHandler`, `GetBalanceQuery`, `CreateAccountHandler`. Não há biblioteca de mediação, porque o endpoint recebe o handler por injeção e a depuração tem um nível de indireção a menos. O `LayerDependencyTests` confere que todo `Handler` é selado, expõe só `HandleAsync` e recebe o `CancellationToken` por último.

No banco, tudo é `snake_case`: tabelas no plural (`accounts`, `ledger_entries`), colunas no singular, chaves estrangeiras `<tabela_no_singular>_id` e instantes em `timestamptz` com o sufixo `_at`. Restrições e índices levam o prefixo do tipo, a tabela e as colunas (`pk_`, `uq_`, `fk_`, `ix_`, `ck_`), como `uq_idempotency_keys_account_id_key`. As tabelas reais estão em [Modelo de dados](../05-contratos/modelo-de-dados.md). As rotas começam em `/v1`, usam substantivos no plural em minúsculas, `kebab-case` e parâmetros em camelCase (`{accountId}`). Nos contratos JSON as propriedades são camelCase, valores monetários são texto decimal (`"100.00"`) e enumerações são texto em maiúsculas. Os códigos de erro são estáveis, em inglês e em `SCREAMING_SNAKE_CASE` ([Catálogo de erros](../05-contratos/catalogo-de-erros.md)), e os eventos têm nome em PascalCase no passado, como `EntryRegistered`.

## Camadas e dependências

O código é organizado por assunto, e não por tipo técnico. Não existem pastas `Services`, `Models` ou `Helpers`: o que muda junto fica junto. Os projetos de teste espelham as pastas do que testam, e o teste de `src/Ledger.Domain/Entries/Entry.cs` fica em `tests/Ledger.Domain.Tests/Entries/EntryTests.cs`. A dependência só aponta para dentro:

| Projeto | Pode referenciar | Não pode |
|---|---|---|
| `Ledger.Domain` | Só a biblioteca base do .NET | Nenhum outro projeto do ledger, `System.Data`, `Npgsql`, `Dapper`, `RabbitMQ.Client`, ASP.NET Core nem `Microsoft.Extensions.*` |
| `Ledger.Application` | `Ledger.Domain` | Qualquer pacote ou projeto de infraestrutura |
| `Ledger.Infrastructure` | `Ledger.Application` e `Ledger.Domain` | `Ledger.Api` e `Ledger.Worker` |
| `Ledger.Api` | `Ledger.Application`, `Ledger.Infrastructure` e `Ledger.Domain` | `Ledger.Worker`, e `Npgsql` ou `Dapper` nos endpoints |
| `Ledger.Worker` | `Ledger.Application`, `Ledger.Infrastructure` e `Ledger.Domain` | `Ledger.Api` |

O `LayerDependencyTests`, do `Ledger.Architecture.Tests`, lê as referências dos assemblies e o uso de tipos. O mesmo projeto confere que só os tipos de persistência usam `Npgsql` e `Dapper`, que as interfaces de porta só têm implementação em `Ledger.Infrastructure` e que a superfície pública da infraestrutura é exatamente a que os executáveis precisam (`InfrastructurePublicSurfaceTests`). `Ledger.Application.Tests` não enxerga a infraestrutura (`TestProjectDependencyTests`).

O que não precisa ser público é `internal`. `Ledger.Domain` e `Ledger.Application` expõem tipos públicos porque as camadas de cima os consomem, e a infraestrutura expõe só o que os executáveis precisam para compor os serviços. `Ledger.Api` e `Ledger.Worker` mantêm tudo `internal` (a CA1515 fica ligada neles), e o que os testes precisam ver chega por `InternalsVisibleTo`, sem tornar tipo público só para ser testado. Classes que não são herdadas nascem `sealed`.

## Erros de negócio com Result

Regra de negócio não lança exceção. Saldo insuficiente, conta inexistente, chave reutilizada com outro corpo e valor inválido acontecem todos os dias e merecem estar na assinatura do método. Tudo o que pode falhar por regra devolve `Result` ou `Result<T>`, definidos em `Ledger.Domain.Shared`. O erro é um `Error(Code, Message, Kind)`, e o `ErrorKind` tem quatro valores: `Validation`, `NotFound`, `Conflict` e `Unprocessable`. Os erros de cada assunto moram numa classe estática ao lado dele (`MoneyErrors`, `EntryErrors`, `AccountErrors`), e `Result<T>` converte implicitamente de `Error` e de `T`. A tradução de `ErrorKind` para status HTTP acontece em um só ponto, o `ProblemFactory` da API: 400, 404, 409 e 422, respectivamente. Os demais status (401, 403, 429, 500 e 503) vêm da autenticação, dos limitadores e do tratamento de exceção.

```csharp
public static Result<Money> Create(decimal amount, string currency)
{
    if (!IsValidCurrency(currency))
    {
        return MoneyErrors.InvalidCurrency;
    }

    if (decimal.Round(amount, DecimalPlaces) != amount)
    {
        return MoneyErrors.TooManyDecimals;
    }

    if (Math.Abs(amount) > MaxAbsoluteAmount)
    {
        return MoneyErrors.OutOfRange;
    }

    return new Money(decimal.Round(amount, DecimalPlaces), currency);
}
```

Exceção fica para defeito (pré-condição violada) e para falha de infraestrutura (tempo esgotado, conexão perdida, broker fora). Ela sobe até o `GlobalExceptionHandler` da API, que devolve 503 com `Retry-After` quando a falha é transitória e 500 em Problem Details, sem detalhe interno, no resto. Um `catch` genérico só existe nessas bordas e no laço de um serviço de fundo, e nunca se escreve `catch` vazio nem se captura exceção para devolver `null`. A violação de unicidade do PostgreSQL (SQLSTATE `23505`) é tratada dentro do repositório e vira `Result`, e é assim que a corrida de duas requisições com a mesma `Idempotency-Key` se resolve sem que a camada de cima saiba da colisão.

## Registros e objetos de valor

Comandos, consultas e respostas são registros selados, posicionais e imutáveis, um por arquivo. Objetos de valor (`Money`, `IdempotencyKey` e os identificadores) têm construtor privado e uma fábrica (`Create` ou `From`) que devolve `Result`, então um valor inválido não nasce pelo caminho normal. Identificadores são tipados (`AccountId`, `EntryId`) para ninguém passar um onde se espera o outro, e como todo `struct` admite `default`, a propriedade `Value` lança `InvalidOperationException` quando o `Guid` está zerado. O texto só é aceito no formato com hífens, conferido posição a posição antes de `Guid.TryParseExact`, que sozinho aceita espaços e prefixos que dariam várias grafias ao mesmo identificador.

Enumerações cujo `default` poderia passar por válido começam em 1 (`EntryType` tem `Credit = 1` e `Debit = 2`). `Money` guarda `decimal` e a moeda, nunca `double`, e recusa valor com mais de duas casas em vez de arredondar em silêncio. `Money.Create` aceita valor negativo, que um saldo pode ter, e `Money.CreatePositive` é o que o lançamento usa, porque o valor de um lançamento é sempre positivo e o sentido da operação vem de `EntryType`. `Entry` é uma classe selada, sem construtor nem setter públicos, criada por `Entry.Credit`, `Entry.Debit` e `Entry.ReversalOf`: nasce completa e nunca muda, como um ledger imutável pede. Os tipos de linha do Dapper ficam privados no repositório, e o domínio nunca ganha construtor público sem validação para agradar um mapeador.

## Async e cancelamento

Todo método que faz E/S é assíncrono, devolve `Task` ou `Task<T>` e recebe `CancellationToken cancellationToken` como último parâmetro, sem valor padrão em `Ledger.Application` e `Ledger.Infrastructure`, para quem chama ter de decidir. O endpoint usa `HttpContext.RequestAborted`, o serviço de fundo usa o `stoppingToken`, e o analisador CA2016 cobra que todo token recebido seja repassado. Com Dapper, o token só chega ao banco por `CommandDefinition`, porque `QueryAsync(sql, parameters)` direto compila e ignora o cancelamento.

O projeto não usa `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, `async void` nem `Task.Run` no caminho de uma requisição, e o trabalho em segundo plano mora no Worker. Também não usa `ConfigureAwait(false)`, porque o ASP.NET Core não tem contexto de sincronização, e o `BannedSymbols.txt` o proíbe. Tempos e retentativas vêm da configuração, nunca de números soltos: o retry das escritas é um `ResiliencePipeline` do Polly 8 (`WriteRetryPipeline`) que só envolve o acesso a dados de escrita, e um trecho que precise de limite próprio usa `CancellationTokenSource.CreateLinkedTokenSource` com `CancelAfter`. Os valores estão em [Políticas de resiliência](../08-resiliencia-e-operacao/politicas-de-resiliencia.md).

## Injeção de dependência, configuração e tempo

Dependências entram por construtor, de preferência primário, e não há localizador de serviço: `IServiceProvider` só aparece na raiz de composição. Cada camada expõe extensões de registro (`AddApplication` na aplicação e métodos `AddLedger*` na infraestrutura, na API e no Worker), e `Program.cs` só compõe. Handlers e repositórios são `Scoped`, a unidade de trabalho é uma por requisição, e os `NpgsqlDataSource` (um por fonte: `Write`, `Balance`, `Statement`, `Worker` e `Migrator`, cada um com seu pool) e os componentes de criptografia são `Singleton`. Não existe estado estático mutável.

A configuração é tipada. Cada seção é ligada com `AddOptions<T>().BindConfiguration(...)`, tem um `IValidateOptions<T>` e usa `ValidateOnStart()`, então valor ausente ou fora da faixa impede o processo de subir em vez de assumir um padrão perigoso. O validador chama `OptionsValidation.Collect`, da camada de aplicação, que lê os atributos de faixa, devolve uma falha por chave com o nome completo da seção e confere as regras que cruzam campos, coisa que a validação por anotações sozinha não faz. O rigor depende do ambiente (`RequiresProductionControls`): só `Development` e `Testing` são permissivos, e qualquer outro nome recebe os controles de produção ([Ambientes e configuração](../10-implantacao-e-entrega/ambientes-e-configuracao.md)). Segredo nunca entra em `appsettings.json`, e o `ConfigurationIsReadTests` confere que toda propriedade configurável é lida pelo código que ela configura.

O relógio é `TimeProvider`, e identificadores vêm do `IIdGenerator`. Por isso `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.UtcNow`, `Thread.Sleep`, `Task.Result`, `ConfigureAwait` e `Guid.NewGuid` estão no `BannedSymbols.txt`, e o analisador quebra a compilação se aparecerem. Os testes usam o `tests/BannedSymbols.txt`, a lista da raiz sem o `Guid.NewGuid`, com o `BannedSymbolsTests` conferindo que as duas não se afastam, e o `FakeTimeProvider` no lugar do relógio.

## Pacotes

Todas as versões moram em `Directory.Packages.props`, e os projetos referenciam o pacote sem versão. O `CentralPackageTransitivePinningEnabled` fixa também as transitivas, e o `nuget.config` deixa só o `nuget.org`. A auditoria do NuGet está ligada em todos os projetos (`NuGetAuditMode` em `all`, nível `low`), e `dotnet list package --vulnerable --include-transitive` lista o que tiver vulnerabilidade conhecida. Os pilares da pilha (banco, broker, acesso a dados, migrações) estão fixados em documento de arquitetura. As imagens base dos Dockerfiles são fixadas por resumo criptográfico, e a atualização delas e dos pacotes é manual, trocando o resumo ou a versão. O único pacote preterido é o `xunit` 2.9.3, marcado no NuGet em favor do `xunit.v3`, e a escolha de ficar nele está no [documento de arquitetura 0021](../03-principios-e-decisoes/documento-arquitetura/0021-plataforma-dotnet-10.md).

## Nulabilidade, formatação e avisos

`Nullable` está habilitado em todos os projetos, e a ausência de valor é dita no tipo: `string?` pode faltar, `string` não. Coleções nunca são `null`, são vazias. O operador `!` de supressão de nulabilidade não entra em `src/`, e o `RoslynSyntaxGuardTests` confere. Propriedades obrigatórias de DTO usam `required`.

O `.editorconfig` da raiz é a fonte da formatação, e a violação de estilo é erro de compilação porque o `Directory.Build.props` liga `EnforceCodeStyleInBuild`. As regras que mais aparecem: indentação de 4 espaços (2 em YAML, JSON e arquivos de projeto), chaves sempre presentes e na linha seguinte, namespace no formato de arquivo, `var` quando o tipo é evidente pelo lado direito, `System` primeiro nos `using`, UTF-8 sem BOM, fim de linha LF e linha em branco ao final do arquivo. O `dotnet format --verify-no-changes` confere tudo.

A solução compila sem erros e sem avisos. O `Directory.Build.props` liga `TreatWarningsAsErrors`, `Nullable`, `AnalysisLevel` em `latest-all` com `AnalysisMode` em `All` e `EnforceCodeStyleInBuild`, e os Dockerfiles compilam com `-warnaserror`. Não existe `#pragma warning disable`, e o `RoslynSyntaxGuardTests` reprova qualquer ocorrência em `src/` e `tests/`.

Poucas regras estão desligadas, e o `.editorconfig` tem a lista completa: CA2007 (o `ConfigureAwait` já é proibido), CA1812 (classes internas são instanciadas pelo contêiner), CA1303 (não há localização), CA1062 (a nulabilidade já cobre), CA1716, CA1724, CA2234, IDE0058, IDE0160 e IDE0073 em todo o código, e CA1707 (o sublinhado faz parte do nome de teste) e CA1515 (o xUnit exige classes de teste públicas) só nos testes, onde o sufixo `Async` também não é exigido. A supressão pontual é por atributo, com `Justification` preenchida:

```csharp
[SuppressMessage("Design", "CA1008", Justification = "A zero member would let an unset entry type pass for a valid one; the default must stay outside the defined values.")]
```

## Comentários

O código C# não tem comentários: nem `//`, nem `/* */`, nem `///`. Diretivas de compilador (`#nullable`, `#if`) e atributos não são comentários, e barras dentro de literal de texto, como uma URL ou um SQL, também não. Quando um trecho parece pedir explicação, extrai-se um método com nome que diga o que ele faz, troca-se o número solto por uma constante nomeada (`MaxDescriptionLength`) ou leva-se a regra para um tipo (`Money`, `IdempotencyKey`). O teste é a explicação que não envelhece, porque quebra quando fica falsa. O porquê de uma decisão vai para o documento de arquitetura, e a documentação das rotas vai para o próprio endpoint, com `WithSummary`, `WithDescription` e `Produces`, que geram o documento OpenAPI. Pendência não vira `TODO` no fonte, e código antigo é removido, não comentado. SQL, YAML e Dockerfile seguem a mesma linha ([documento de arquitetura 0015](../03-principios-e-decisoes/documento-arquitetura/0015-sem-comentarios-e-zero-warnings.md)).

## Dados, logs e testes

O SQL das partes críticas é explícito e mora ao lado do repositório que o usa, em literais de texto bruto (`"""`) dentro de constantes privadas, uma por operação. Os parâmetros são sempre nomeados, interpolar ou concatenar valor em SQL é falha de segurança, e as consultas listam as colunas, sem `*`. Decisão que depende de estado concorrente (saldo, idempotência, unicidade) é tomada pelo banco em uma instrução atômica, e não por leitura seguida de escrita no código. O handler não abre conexão: nas escritas ela vem da unidade de trabalho, que controla a transação, e nas leituras o repositório de leitura abre a sua. O `CriticalSqlMatchesFlowPagesTests` e o `ReadSqlMatchesFlowPagesTests` comparam as constantes de SQL com os blocos `sql` das páginas de [fluxos](../06-fluxos/README.md).

As migrações do DbUp são arquivos `.sql` embutidos como recurso em `src/Ledger.Infrastructure/Persistence/Migrations/`, nomeados `NNNN_descricao_em_ingles.sql`. Migração aplicada nunca é editada: corrigir é escrever a próxima, e o `migrations.sha256`, com o `MigrationChecksumTests`, derruba a suíte se uma for alterada. Não existe script de volta, porque reverter também é uma migração para a frente. Um teste de integração aplica todas, do zero, num banco vazio (`MigrationTests`). O fluxo está em [Migração do esquema](../06-fluxos/migracao-do-esquema.md).

Logs usam mensagens geradas por `[LoggerMessage]` em métodos `partial`, com identificador de evento e parâmetros nomeados em PascalCase (`{AccountId}`). As regras CA1848 e CA2254 barram o log por interpolação de texto, e a CA1873 barra o argumento que custa calcular com o nível desligado. Documento do titular, nome e qualquer dado pessoal não aparecem em log, métrica nem rastro de execução ([Saúde e observabilidade](../08-resiliencia-e-operacao/saude-e-observabilidade.md), [Catálogo de métricas](../08-resiliencia-e-operacao/catalogo-de-metricas.md)).

Todo endpoint tem escopo de autorização explícito, resposta de erro em Problem Details, `WithSummary` e `Produces`, e o documento OpenAPI versionado acompanha o contrato (`OpenApiContractTests`). Toda operação que toca o banco ou o broker tem prazo, e as que escrevem têm o caso de repetição coberto por teste.

Os testes seguem `Sujeito_Condicao_Resultado`, em inglês, como `Apply_DebitOneCentAboveTheBalanceWithZeroLimit_ReturnsInsufficientFunds`, e cada um cobre um comportamento, com preparação, ação e verificação separadas por linhas em branco. A verificação usa Shouldly, o NSubstitute dubla as portas da camada de aplicação e nenhuma classe do ledger, os testes de domínio não usam dublê e SQL roda contra um PostgreSQL de verdade, via Testcontainers. Teste que depende de ambiente falha quando o ambiente falta, com mensagem que ensina a subi-lo, e só o opt-out explícito o ignora ([documento de arquitetura 0022](../03-principios-e-decisoes/documento-arquitetura/0022-testes-dependentes-de-ambiente-falham-por-padrao.md)). Cada teste de integração cria as próprias contas em vez de limpar o banco, e nenhum usa `Thread.Sleep`: espera-se por condição, com prazo. As camadas de teste e os perfis de execução estão em [Estratégia de testes](../09-qualidade/estrategia-de-testes.md).

## Leia também

- [Princípios de arquitetura](../03-principios-e-decisoes/principios-de-arquitetura.md): o que cada regra protege.
- [Garantias automáticas](../09-qualidade/portoes-de-qualidade.md): as conferências de build e de documentação.
- [C4 nível 3: componentes da API](../04-modelos-c4/nivel-3-componentes-api.md): a estrutura da API que as pastas refletem.
