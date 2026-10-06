# Estratégia de testes

O que se testa, em que camada, com quais ferramentas e por quê, e como cada grupo roda. Os testes de concorrência e de falha, que mais pesam num ledger, têm página própria: [Testes de concorrência e de falha](testes-de-concorrencia-e-falha.md). O que o build e os testes de arquitetura barram antes dos demais está em [Garantias automáticas](portoes-de-qualidade.md), e o que ainda não foi medido nem exercitado, em [Limites conhecidos](limites-conhecidos.md).

## A forma da pirâmide

O sistema não tem frontend, o que elimina a camada de testes de interface e muda onde está o risco. A lógica de domínio de um ledger é pequena e fácil de testar. O que o quebra em produção é concorrência, falha parcial e repetição: duas requisições na mesma conta, uma conexão que cai no meio da transação, um Worker que morre depois de publicar e antes de marcar. Nada disso aparece com dublês, porque o comportamento que importa mora no PostgreSQL. Por isso o centro de gravidade é a integração contra banco e broker reais, e o desenho é mais um losango que uma pirâmide ([documento de arquitetura 0014](../03-principios-e-decisoes/documento-arquitetura/0014-estrategia-de-testes.md)).

| Camada | Projeto | Roda contra | Prova | Não prova |
|---|---|---|---|---|
| Unidade de domínio | `Ledger.Domain.Tests` | Nada | Regras de negócio: valor positivo, limite de cheque especial, estorno, normalização do documento | Que o SQL faz o mesmo que o objeto em memória |
| Unidade de aplicação | `Ledger.Application.Tests` | NSubstitute, relógio falso | Orquestração: ordem dos passos, ramos de erro, idempotência no nível do fluxo | Comportamento do banco |
| Unidade de infraestrutura | `Ledger.Infrastructure.Tests` | Dublês, sem E/S externa | Cifra, índice cego, provedor de chaves, telemetria, classificação de falhas | Comportamento contra banco ou broker |
| Arquitetura | `Ledger.Architecture.Tests` | Assemblies e arquivos do repositório | Camadas, convenções estruturais e documentação coerente com o código | Comportamento em execução |
| Integração | `Ledger.Api.IntegrationTests` | PostgreSQL 16 e RabbitMQ 3 reais (Testcontainers) | SQL, restrições, gatilhos, transação, concorrência real, contrato HTTP, autenticação, outbox com broker real | Rede entre contêineres, orquestração |
| Fim a fim | `Ledger.EndToEnd.Tests` | Docker Compose completo | Imagens, configuração e rede juntas, e a recuperação quando um componente cai | O que a integração já cobre em detalhe |
| Carga | `tests/load/ledger.k6.js` | Docker Compose completo | Rotas e invariantes sustentando o volume da escala executada | Capacidade em produção |

## Unidade

O domínio é puro, sem banco, relógio nem E/S, com xUnit e Shouldly e sem dublê. Os testes cobrem `Money` (`MoneyTests`: duas casas, negativos, zero, moedas diferentes), o lançamento (`EntryTests`, que confere por reflexão que a classe é selada e sem construtor nem setter públicos), o saldo (`AccountBalanceTests`: a regra `saldo + delta >= -limite` abaixo, exatamente no limite e acima), o estorno (`ReversalCandidateTests`), o documento do titular (`HolderDocumentTests`, `HolderDocumentMaskTests`) e o extrato (`StatementBoundsTests`, `StatementPositionTests`). O `BalanceSequenceTests` aplica 10.000 operações aleatórias, com semente fixa, ao modelo em memória e confere que o saldo termina igual à soma dos valores assinados e nunca ficou abaixo do limite. É a versão barata do invariante central, e a mesma verificação roda contra o PostgreSQL na integração (`ParallelDebitsTests`, `ParallelEntriesPreserveSumTests`). Não há biblioteca de testes baseados em propriedades: o único gerador que importa, a sequência de operações, é simples de escrever com semente fixa.

Na aplicação ficam os casos de uso, com as portas substituídas por NSubstitute e um `FakeTimeProvider`. Os testes provam a coreografia ramo a ramo (`RegisterEntryHandlerTests`, `RegisterEntryRefusalTests`, `RegisterEntryReplayTests`, `ReverseEntryHandlerTests`, `CreateAccountHandlerTests`, `CanonicalRequestHashTests`). A repetição com a mesma chave devolve o resultado guardado sem tocar o repositório, e chave igual com corpo diferente devolve o conflito. Saldo insuficiente não escreve nada nem confirma a transação. O caminho feliz grava lançamento, saldo, evento e chave nessa ordem e na mesma unidade de trabalho, e falha ao confirmar não produz sucesso. A criação de conta cifra o documento com o `account_id` como dado associado e não o deixa chegar ao log. Um dublê só prova que o handler chamou o que devia. Que o SQL fez o que ele esperava é trabalho da integração, por isso não há teste de concorrência nesta camada.

Os testes da infraestrutura que não dependem de E/S externa moram em `Ledger.Infrastructure.Tests` ([documento de arquitetura 0024](../03-principios-e-decisoes/documento-arquitetura/0024-testes-de-unidade-da-infraestrutura-em-projeto-proprio.md)), e o `Ledger.Application.Tests` referencia só a aplicação e o domínio. O que é `internal` e precisa de teste chega por `InternalsVisibleTo`, sem tipo público só para teste. O projeto roda em série (`DisableTestParallelization`), porque a telemetria usa fontes e medidores globais ao processo. O `DocumentPayloadFormatTests` é um teste de resposta conhecida: com chave, nonce, texto e dado associado fixos, o blob cifrado precisa ser exatamente um valor publicado no teste, o que garante que os dados cifrados hoje continuarão legíveis. O `InstrumentCatalogTests` compara os instrumentos com o [Catálogo de métricas](../08-resiliencia-e-operacao/catalogo-de-metricas.md) nos dois sentidos.

## Integração

É a camada central. O `Ledger.Api.IntegrationTests` sobe a API inteira em memória com `WebApplicationFactory<Program>`, ligada a um PostgreSQL 16 criado pelo Testcontainers com os papéis do `init-roles.sh`, e o RabbitMQ 3 entra do mesmo jeito pelo `RabbitMqFixture`. Os contêineres sobem uma vez por execução, num `ICollectionFixture`, e as migrações do DbUp rodam na subida, o que as testa a partir de um banco vazio.

Cada teste cria as próprias contas, com identificadores novos, e não limpa nada depois. Assim um teste não atrapalha o outro, e se evita truncar tabelas, o que num ledger só de inserção esconderia justamente o tipo de defeito que se procura. Em contrapartida, nenhum teste assume banco vazio nem que uma contagem global é a sua. Os testes de banco rodam em série na coleção `PostgreSQL`, e a concorrência que interessa é a que o teste provoca com o `ParallelGate`. O projeto desliga o paralelismo entre coleções porque o `ActivitySource` do ASP.NET Core, os medidores `Ledger` e as variáveis `OTEL_*` pertencem ao processo e não a cada host: dois hosts vivos contariam os spans um do outro ([documento de arquitetura 0034](../03-principios-e-decisoes/documento-arquitetura/0034-suite-de-integracao-em-serie-e-perfil-rapido.md)). O que depende de assincronia, como o outbox publicando, não usa `Thread.Sleep`: o `ConditionWait.UntilAsync` repete a verificação a cada 100 ms até passar ou estourar o prazo e, ao estourar, diz o que esperava. Teste instável não se desliga nem se ignora: ou está mal escrito e se corrige, ou aponta uma corrida real no produto.

A autenticação não usa emissor real nos testes. A maior parte da suíte põe a API no modo `LocalKey`, e a `TestTokenFactory` assina JWTs HS256 com `iss` `https://idp.test`, `aud` `ledger-api` e as claims `client_id` e `scope` que o teste pedir, de modo que a validação de verdade (assinatura, audiência, emissor, expiração) roda contra um emissor controlado. O HS256 existe só nesse modo, que o validador de opções recusa fora de `Development` e `Testing`. A validação de produção (RS256 ou ES256) tem testes à parte: o `RealKeyJwtTests` assina com um par de chaves de verdade, o `FakeIssuer` serve um JWKS por HTTP para o modo `Authority` (`JwksCacheTests`, `JwtValidationTests`), e o fim a fim cobre RS256 com um par de chaves de verdade ([Autenticação e autorização](../07-consistencia-e-seguranca/autenticacao-e-autorizacao.md)).

Segurança, cifra e chaves, criação de conta com chave, outbox, observabilidade e integridade têm os testes citados nas páginas de cada tema ([Segurança](../07-consistencia-e-seguranca/seguranca.md), [Proteção de dados](../07-consistencia-e-seguranca/protecao-de-dados.md), [Cenários de falha](../08-resiliencia-e-operacao/cenarios-de-falha.md) e [Saúde e observabilidade](../08-resiliencia-e-operacao/saude-e-observabilidade.md)). Os demais, por grupo:

| Grupo | Testes |
|---|---|
| Persistência | `MigrationTests` (migração do zero e repetida, execução concorrente com trava, `UPDATE`, `DELETE` e `TRUNCATE` rejeitados em `ledger_entries` e `audit_log`), `RolePrivilegesTests`, `QueryPlanTests` (com 100.000 linhas: saldo em um instante por varredura só de índice, extrato andando no índice sem ordenar), `InvariantVerifierTests` |
| Contrato HTTP | Conta, lançamento, estorno, repetição, saldo atual e em um instante, janela de acomodação, extrato e validação: `CreateAccountTests`, `RegisterEntryTests`, `ReverseEntryTests`, `IdempotentReplayTests`, `CurrentBalanceTests`, `AsOfBalanceTests`, `SettledWindowTests`, `StatementPaginationTests`, `EntryValidationTests`, `ReadValidationTests`, `ProblemDetailsTests` |
| Datas e fusos | `InstantReadingTests`, `EntryTimeZoneTests`, `BalanceTimeZoneTests`, `StatementTimeZoneTests`, `NpgsqlInfinityConversionTests` e `InstantOutputTests`: leitura em UTC e com deslocamento, o piso de `occurredAt`, a recusa do instante sem fuso e a saída sempre em UTC com `Z` ([documento de arquitetura 0036](../03-principios-e-decisoes/documento-arquitetura/0036-politica-de-fusos-horarios.md)) |
| Mensagens em português | `FieldIssuesTests`, `ProblemCatalogTests` e `ProblemDetailsTests` (texto exato de cada `title`, `detail` e mensagem por campo, UTF-8 legível, `charset` no tipo de mídia) e, nos testes do domínio, o `MessageStyle`, que reprova mensagem sem ponto, com palavra em inglês ou com palavra sem acento ([documento de arquitetura 0037](../03-principios-e-decisoes/documento-arquitetura/0037-idioma-das-mensagens-ao-chamador.md)) |
| Composição dos executáveis | `HostCompositionTests`: API e Worker sobem com todas as registrações validadas, contra PostgreSQL e RabbitMQ reais. Em `Development` e `Testing`, toda porta e todo caso de uso são instanciáveis e o `ready` responde `Healthy`. Em `Production`, `Staging` e `Homolog` valem as regras de produção, e o `ready` fica 503 enquanto não há TLS verificado com o banco |
| Contrato OpenAPI | `OpenApiContractTests` (o documento gerado é igual a `docs/05-contratos/openapi.v1.json`) e `OpenApiExposureTests` (a rota só existe em `Development` e `Testing`) |
| Concorrência e falha | [Testes de concorrência e de falha](testes-de-concorrencia-e-falha.md) |

## Arquitetura

O `Ledger.Architecture.Tests` transforma regras do desenho em teste com o NetArchTest e lê também os arquivos do repositório: se alguém importar `Npgsql` no domínio a suíte quebra, e o mesmo acontece se o SQL de uma página de fluxo divergir do código. A lista do que cada teste reprova está em [Garantias automáticas](portoes-de-qualidade.md).

## Fim a fim

O `Ledger.EndToEnd.Tests` usa `HttpClient` puro contra a API que subiu com Docker Compose, sem `WebApplicationFactory`, porque o ponto da camada é provar que imagens, configuração e rede funcionam juntas. A pilha chega ao teste de duas maneiras. Com `LEDGER_E2E_PROVISION=true`, o fixture provisiona uma pilha própria, com segredos aleatórios, par RSA em memória, portas livres e nome de projeto isolado, e a derruba no fim, volumes incluídos ([documento de arquitetura 0032](../03-principios-e-decisoes/documento-arquitetura/0032-teste-fim-a-fim-que-provisiona-o-proprio-ambiente.md)). Sem a variável, o teste se liga a um ambiente já de pé: `LEDGER_E2E_BASE_URL` (padrão `http://localhost:8080`) aponta a API, as credenciais do banco e do RabbitMQ saem do `.env`, o token é assinado com `dev-keys/private.pem` (ou com a chave de `LEDGER_E2E_SIGNING_KEY_PATH`) e `LEDGER_E2E_COMPOSE_PROJECT` (padrão `ledger-test`) diz de que projeto do Compose são os contêineres que os testes de resiliência param e religam. Esse ambiente deve ter subido com o `docker-compose.test.yml` por cima do `docker-compose.yml`, que dá ao projeto o nome `ledger-test`, relaxa os limites de taxa e encurta o intervalo da conferência de integridade. O `HttpClient` dos testes tem prazo de 30 segundos. `LEDGER_E2E_WORKER_URL` (padrão `http://localhost:8081`) e `LEDGER_E2E_RABBITMQ_MANAGEMENT_URL` (padrão `http://localhost:15672`) precisam ser URLs absolutas `http` ou `https`.

O `E2ETokenFactory` assina RS256 com `kid` `dev-local`, emissor `https://idp.local.test` e audiência `ledger-api`, como o token da [Execução local](../10-implantacao-e-entrega/execucao-local.md), e também emite token vencido, de outra audiência ou assinado por outra chave. As contas nascem pela rota `POST /v1/accounts`, pelo `E2EAccountSeeder`, e não por `INSERT`, para o documento passar pela cifra de verdade. Para o que a API não mostra, os testes olham por baixo: o banco, com o papel `ledger_worker` (o teste de adulteração de saldo usa uma conexão de administrador, porque nenhum papel da aplicação altera saldo), e o RabbitMQ pela API de gerenciamento, com uma fila durável própria ligada à troca `ledger.events`. O `ComposeControl` para, sobe, mata e reinicia serviços pelo `docker compose`, e nenhum teste usa `docker exec`.

| Classes | O que provam |
|---|---|
| `HealthE2ETests`, `ComposeSmokeE2ETests` | `live` e `ready` da API e do Worker em 200, o `migrator` terminou com código 0, o `X-Correlation-Id` do chamador volta e o inválido é trocado |
| `AccountsE2ETests` | Conta criada devolve só o documento mascarado e o banco o guarda cifrado, saldo começa em zero, pedido inválido é 400 com o nome do campo |
| `EntriesE2ETests`, `EntriesRulesE2ETests`, `EntriesConcurrencyE2ETests` | Crédito, débito, repetição, chave reutilizada, saldo insuficiente, moeda trocada, estorno e estorno duplo, repetições simultâneas gravando uma vez só |
| `ReadsE2ETests`, `BalanceInstantE2ETests`, `StatementE2ETests` | Saldo atual e em um instante do passado, `settled` falso dentro da janela e verdadeiro depois, extrato paginado sem repetir nem faltar |
| `AuthenticationE2ETests` | 401 sem token válido, 403 com o escopo trocado, e o `client_id` do token é o que fica gravado |
| `EntryEventsE2ETests`, `RetentionQueueE2ETests` | Cada lançamento chega uma vez a uma fila de teste, com o `X-Correlation-Id` do pedido e sem o documento do titular, e uma escrita recusada não publica evento. A fila de retenção é durável, do tipo `quorum`, e guarda cada lançamento uma vez |
| `EntriesLatencyE2ETests`, `ReadsLatencyE2ETests`, `EventLatencyE2ETests` | Os orçamentos de latência (categoria `Latency`), multiplicados por `LEDGER_E2E_LATENCY_FACTOR`, e o atraso entre o `recordedAt` do evento e a chegada à fila |
| `IntegrityE2ETests` | Com o Worker do Compose conferindo a cada minuto, uma execução recente fica registrada com zero violações, e um saldo adulterado vira `integrity.violation_detected` sem nada ser corrigido |

Os testes de `Category=Resilience` derrubam componentes do Compose de verdade, por isso rodam à parte e deixam a pilha como a encontraram: `PostgresOutageE2ETests`, `ReadsResilienceE2ETests`, `BrokerOutageE2ETests`, `WorkerRestartE2ETests` e `ApiKillE2ETests`.

## Carga

Os cenários ficam em `tests/load/ledger.k6.js` e rodam com o k6 em contêiner, dentro do Compose de teste. O token é o da [Execução local](../10-implantacao-e-entrega/execucao-local.md):

```bash
LEDGER_TOKEN=$TOKEN K6_SCENARIO=smoke docker compose -f docker-compose.yml -f docker-compose.test.yml --profile load up --build --exit-code-from k6 k6
```

O código de saída é o do k6, de modo que um limiar estourado reprova o comando. `K6_SCENARIO` escolhe o cenário, `LOAD_SCALE` a escala (o Compose de teste assume 0,1), `K6_LATENCY_FACTOR` multiplica os limiares de latência, `K6_HOT_ACCOUNT_RATE` fixa o ritmo da conta quente e `K6_VERIFIED_SAMPLE` o tamanho da amostra de contas conferidas no fim. O `docker-compose.test.yml` também relaxa os limites de taxa da API, para a carga não ser barrada por eles. Para derrubar a pilha depois, `docker compose -f docker-compose.yml -f docker-compose.test.yml --profile load down -v`.

| Cenário | Perfil | Limiares |
|---|---|---|
| `smoke` | 30 s, 2 jornadas completas por segundo, todas as rotas, com estorno e verificação de invariantes ao fim de cada jornada | p99 abaixo de 150 ms nas escritas e de 50 ms no saldo, nenhum erro inesperado |
| `write-spread` | Rampa até 2.000 lançamentos por segundo em 10.000 contas (vezes a escala), com 1% de repetições de chave | p99 abaixo de 150 ms, menos de 0,05% de respostas inesperadas |
| `write-hot-account` | 100 lançamentos por segundo numa conta só (`K6_HOT_ACCOUNT_RATE`), por 5 minutos | p99 abaixo de 150 ms e saldo final igual à soma |
| `balance-read` | Rampa até 10.000 consultas por segundo, 5% com `asOf` | p99 abaixo de 50 ms |
| `statement-read` | Rampa até 500 extratos por segundo, cada um com duas páginas de 100 itens | p99 abaixo de 200 ms |
| `mixed` | Os quatro de volume ao mesmo tempo | Os limiares dos quatro |

Só 200 e 201 contam como resposta esperada. O `smoke` declara 401, 422 e 409 apenas nas chamadas em que confere a recusa de propósito (requisição sem token, chave reutilizada, saldo insuficiente, segundo estorno), de modo que um 422 inesperado no meio de uma escrita aparece como falha. Todo cenário termina no `teardown`, que confere por HTTP, sem ajuda do banco, a conta quente e uma amostra das demais: versões sem lacuna, `balanceAfter` encadeado e saldo atual igual à soma do extrato. Cada divergência incrementa `ledger_invariant_violations`, e o limiar `count==0` reprova a execução. Nos cenários de escrita vale também a conservação: a soma dos saldos é o financiamento inicial mais os créditos aceitos menos os débitos aceitos, lidos de `ledger_credited_cents` e `ledger_debited_cents`. Nos de volume, o k6 consulta o `/health/ready` do Worker uma vez por segundo, e `ledger_outbox_lag_ok` exige mais de 99% de respostas saudáveis. Carga que passa nos tempos e deixa o saldo errado é falha.

O fator de latência existe porque uma máquina de build hospedada divide o processador com a pilha inteira. Com o k6, a API, o Worker, o PostgreSQL e o RabbitMQ na mesma máquina, a escala cheia não é realista: a de 0,1 serve para flagrar regressões grosseiras, e os números de capacidade só valem em ambiente dedicado ([Limites conhecidos](limites-conhecidos.md)).

## Falhar por padrão quando o ambiente falta

Teste que depende de ambiente falha quando ele falta, com uma mensagem que diz o que fazer ([documento de arquitetura 0022](../03-principios-e-decisoes/documento-arquitetura/0022-testes-dependentes-de-ambiente-falham-por-padrao.md)). Os testes de integração que precisam do Docker levam `[DockerFact]` ou `[DockerTheory]`, que não sondam o Docker na descoberta: quem sonda, uma vez, é o `PostgresFixture`. O fim a fim falha se a API não responde em `/health/ready` em 60 segundos. Existem dois opt-outs e nenhum outro: `LEDGER_TESTS_DISABLE_DOCKER=true` ignora os testes de integração com Docker e `LEDGER_SKIP_E2E=true` ignora o fim a fim, e os dois aparecem como ignorados, com o motivo no resultado. Com `LEDGER_REQUIRE_DOCKER=true`, que os dois pipelines definem, os dois são desconsiderados. A regra tem teste próprio, o `DockerFactAttributeTests`.

## Categorias e perfis

Os testes levam `[Trait("Category", ...)]` para qualquer recorte: `Unit`, `Architecture`, `Integration`, `Concurrency`, `Resilience`, `Security`, `Contract`, `Latency`, `Performance` e `E2E`. O marcador `[Trait("Speed", "Slow")]` está nas cinco classes cujo tempo vem de esperar queda e religação reais de banco ou broker: `BrokerOutageTests`, `PartialConfirmationTests`, `WorkerDatabaseOutageTests`, `BrokerConnectionTests` e `PublisherKilledBetweenPublishAndMarkTests`. Os cenários de carga não são testes do xUnit e não levam categoria.

O perfil completo é o padrão: roda tudo, com três repetições nos testes de concorrência (`CONCURRENCY_ITERATIONS`) e os cenários em que o broker e o banco caem e voltam de verdade, e vale antes de integrar mudança no publicador, na reconexão, nos prazos ou nos pools. O rápido roda a integração com `Speed!=Slow`, usa `CONCURRENCY_ITERATIONS=1` e, no fim a fim, tira a categoria `Resilience`. Serve ao ciclo de trabalho e não substitui o completo.

## Dados de teste

Nenhum teste usa dado real, e nenhum ambiente de teste recebe cópia de produção. Documentos válidos são gerados, não digitados: o `HolderDocumentGenerator` (domínio) e o `CpfFactory` (integração) calculam os dígitos verificadores, e há um conjunto fixo de inválidos. Aleatoriedade tem sempre semente fixa (`ConcurrencyScenarios.RandomSeed`), para a falha ser reproduzível, e valores monetários têm sempre duas casas. O relógio dos testes de aplicação é um `FakeTimeProvider`, e na integração o instante é o do banco, com asserções sobre os `recorded_at` devolvidos e nunca sobre o relógio do teste. Chaves de cifra são geradas em memória a cada execução, exceto no `DocumentPayloadFormatTests`, e cada `client_id` de teste é único, o que permite conferir métricas e auditoria por chamador sem interferência. O Compose local não semeia nada, porque dado semeado esconde dependências de ordem entre testes.

## Cobertura

A cobertura sai do `coverlet.msbuild`. `Ledger.Domain` tem piso de 95% de linhas e 90% de ramos, e `Ledger.Application`, de 90% e 80%. O próprio comando de teste falha abaixo do piso:

```bash
dotnet test tests/Ledger.Domain.Tests -p:CollectCoverage=true -p:Include="[Ledger.Domain]*" -p:Threshold=95%2C90 -p:ThresholdType=line%2Cbranch
dotnet test tests/Ledger.Application.Tests -p:CollectCoverage=true -p:Include="[Ledger.Application]*" -p:Threshold=90%2C80 -p:ThresholdType=line%2Cbranch
```

`Ledger.Infrastructure` e `Ledger.Api` não têm piso, porque são cobertos pela integração, cujo número conta menos: uma linha de SQL executada por um teste que não verifica nada é cobertura sem prova. Domínio e aplicação são código puro e determinístico, onde um buraco de cobertura quase sempre é uma regra sem teste. Por isso não há percentual global como meta, que convida a escrever teste para o número e não para o risco. O teste de mutação responderia melhor à pergunta "estes testes pegariam um defeito?", e não está no pipeline pelo tempo que consome ([Evolução futura](../11-evolucao/evolucao-futura.md)).

## Como rodar

São necessários o SDK do .NET 10 e, para integração, fim a fim e carga, o Docker com Compose v2. Os comandos valem a partir da raiz. O ambiente local e o token estão em [Execução local](../10-implantacao-e-entrega/execucao-local.md), e a execução nos pipelines, em [Pipeline no Azure DevOps](../10-implantacao-e-entrega/pipeline-azure-devops.md).

| Grupo | Comando |
|---|---|
| Compilação sem avisos | `dotnet build -c Release` |
| Formatação | `dotnet format --verify-no-changes` |
| Unidade e arquitetura, sem Docker | `dotnet test --filter "FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~EndToEnd"` |
| Integração, completa | `dotnet test tests/Ledger.Api.IntegrationTests` |
| Integração, perfil rápido | `dotnet test tests/Ledger.Api.IntegrationTests --filter "Speed!=Slow"` |
| Só concorrência | `dotnet test tests/Ledger.Api.IntegrationTests --filter "Category=Concurrency"` |
| Concorrência exaustiva | `CONCURRENCY_ITERATIONS=25 dotnet test tests/Ledger.Api.IntegrationTests --filter "Category=Concurrency"` |
| Resiliência, sem a API | `dotnet test tests/Ledger.Api.IntegrationTests --filter "Category=Resilience"` |
| Fim a fim | `LEDGER_E2E_PROVISION=true dotnet test tests/Ledger.EndToEnd.Tests` |
| Fim a fim, sem resiliência | `LEDGER_E2E_PROVISION=true dotnet test tests/Ledger.EndToEnd.Tests --filter "Category!=Resilience"` |
| Resiliência, fim a fim | `LEDGER_E2E_PROVISION=true dotnet test tests/Ledger.EndToEnd.Tests --filter "Category=Resilience"` |
| Carga e cobertura | Nas seções [Carga](#carga) e [Cobertura](#cobertura) |

Sem filtro, `dotnet test` roda também os projetos de integração e de fim a fim, que precisam de Docker, e o filtro de unidade e arquitetura os exclui pelo nome.

## O que a estratégia não cobre

Os testes provam a queda e a recuperação de uma instância, contra um emissor de tokens falso que fala o mesmo protocolo e contra um provedor de chaves que falha ou lê uma pasta de segredos. Ficam sem exercício a vazão e a latência nas metas, o failover e o RTO de 15 minutos, o cofre de chaves e o emissor de tokens reais, o TLS efetivo, o disco cheio e a execução dos pipelines no serviço. [Limites conhecidos](limites-conhecidos.md) diz, para cada um, como eu o provaria e o que mudaria no desenho se a hipótese caísse. Também não existe teste de carga de longa duração, de horas.
