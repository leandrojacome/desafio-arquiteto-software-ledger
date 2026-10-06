# Ambientes e configuração

O ledger roda em desenvolvimento, em teste e em produção. A página mostra o que muda de um para o outro, o que o Compose sobe, o que a subida recusa fora de desenvolvimento e como a migração do esquema entra na implantação. As chaves, com padrões e validadores, estão em [Configuração e linha de comando](../05-contratos/configuracao.md), o passo a passo local em [Execução local](execucao-local.md) e a topologia de produção, com standby e balanceador, que o repositório não sobe, em [Implantação: topologia de produção](../04-modelos-c4/implantacao-producao.md).

## Os ambientes

A API e o Worker decidem o rigor da configuração pelo nome do ambiente do host (`ASPNETCORE_ENVIRONMENT`). Só `Development` e `Testing` são permissivos. Qualquer outro nome, como `Staging` ou `Production`, recebe os controles de produção: `RequiresProductionControls` devolve verdadeiro para ele, e os validadores de opções recusam a subida quando um valor local escapa para fora do desenvolvimento.

| Ambiente | Onde roda | O que o caracteriza |
|---|---|---|
| Desenvolvimento | Docker Compose local (`docker-compose.yml`), ou `dotnet run` na máquina | `ASPNETCORE_ENVIRONMENT=Development`. Autenticação com chave local, banco sem TLS, detalhe de erro do banco ligado, senhas de desenvolvimento do `.env` |
| Teste | Compose com a camada `docker-compose.test.yml` por cima, e os hosts em memória dos testes de integração (`Testing`) | As permissões do desenvolvimento, com os ajustes da camada de teste descrita mais abaixo |
| Produção e equivalentes | A topologia descrita em [Implantação: topologia de produção](../04-modelos-c4/implantacao-producao.md), que o repositório não sobe | Controles de produção ligados: o nome do ambiente não é `Development` nem `Testing` |

A diferença entre desenvolvimento e produção se concentra em poucas chaves. Os validadores de opções recusam a subida com o código 3 e uma linha `Fatal` que nomeia a chave, o `KeyProviderStartupGuard` a interrompe com uma exceção, e a rota do documento OpenAPI simplesmente não é registrada.

| Assunto | Desenvolvimento | Fora de `Development` e `Testing` | Quem exige |
|---|---|---|---|
| TLS do banco | `Postgres:SslMode` igual a `Disable` | `VerifyFull` obrigatório | `PostgresOptionsValidator` |
| Detalhe de erro do banco | `Postgres:IncludeErrorDetail` igual a `true` | `false` obrigatório | `PostgresOptionsValidator` |
| Modo de autenticação | `Authentication:Mode` igual a `LocalKey`, com a chave pública gerada localmente | `Authority` obrigatório, e `LocalKey` é recusado | `JwtAuthenticationOptionsValidator` |
| Emissor de tokens | `Authentication:Issuer` igual a `https://idp.local.test` | `Authentication:Authority` em `https`, e `RequireHttpsMetadata` igual a `true` | `JwtAuthenticationOptionsValidator` |
| Vida máxima do token | `Authentication:MaxTokenLifetimeMinutes` igual a 240 | No máximo 60 (o padrão é 15) | `JwtAuthenticationOptionsValidator` |
| Criação de conta | `Authorization:AccountProvisioningClients` igual a `*` | Lista explícita de clientes, e `*` é recusado | `ProvisioningOptionsValidator` |
| TLS do broker | `RabbitMq:UseTls` igual a `false` | `true` obrigatório | `RabbitMqOptionsValidator` |
| Limites de taxa | Ligados por padrão, desligáveis | `RateLimiting:Enabled` igual a `true` e `ReplenishmentSeconds` igual a 1 | `RateLimitingOptionsValidator` |
| Provedor das chaves de dados pessoais | `Security:Pii:Provider` igual a `Configuration`, com as chaves em variáveis de ambiente | `Directory` obrigatório, com `Security:Pii:Directory`, e `Configuration` é recusado | `KeyProviderStartupGuard` |
| Documento OpenAPI | Servido em `GET /openapi/v1.json`, sem token | A rota não existe | `OpenApiEndpoints` |

O modo de autenticação está em [Autenticação e autorização](../07-consistencia-e-seguranca/autenticacao-e-autorizacao.md), e as chaves de dados pessoais em [Proteção de dados](../07-consistencia-e-seguranca/protecao-de-dados.md). O provedor `Directory` lê as chaves de uma pasta de segredos e a recarrega a cada `Security:Pii:ReloadMinutes` (10 por padrão). Não existe adaptador para um cofre externo, então onde essa pasta é montada e como as chaves são rotacionadas fica por conta de quem implanta ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)). O TLS efetivo das conexões também não foi exercitado: os validadores só recusam a configuração sem TLS, o Compose roda com `Postgres__SslMode` em `Disable` e `RabbitMq__UseTls` em `false`, e nenhum teste abre uma conexão TLS real.

## Segredos

Segredo nunca entra em `appsettings.json` nem no repositório. Em desenvolvimento os valores vêm do `.env`, copiado do `.env.example` (senhas e chaves fracas de propósito, e o `.gitignore` deixa o `.env` de fora), e chegam aos contêineres por variável de ambiente. Fora do desenvolvimento vêm do mecanismo de segredos da plataforma, também por variável de ambiente ou pela pasta de segredos das chaves de dados pessoais.

| Segredo | Variável no Compose | Observação |
|---|---|---|
| Senhas dos papéis do PostgreSQL | `POSTGRES_SUPERUSER_PASSWORD`, `LEDGER_MIGRATOR_PASSWORD`, `LEDGER_API_PASSWORD`, `LEDGER_WORKER_PASSWORD`, `LEDGER_READONLY_PASSWORD` | Sem valor padrão no Compose: se faltarem, ele para e pede o `.env`, para que nenhuma senha padrão escondida no arquivo chegue à produção |
| Senha do RabbitMQ | `RABBITMQ_PASSWORD` (usuário em `RABBITMQ_USERNAME`) | Sem valor padrão |
| Chaves de dados pessoais | `PII_ENCRYPTION_KEY`, `PII_BLIND_INDEX_KEY` | 32 bytes em base64, entregues como `Security__Pii__KeySets__1__EncryptionKey` e `...BlindIndexKey`, com `Security__Pii__ActiveKeyVersion` igual a 1 |
| Chave do cursor do extrato | `CURSOR_SIGNING_KEY` | 32 bytes em base64, entregue como `Security__Cursor__SigningKey` |
| Chave pública do emissor local | `DEV_JWT_PUBLIC_KEY_PEM_B64` | Só desenvolvimento. O serviço `dev-keys` a grava num volume, e a API a lê em `Authentication:LocalKey:PublicKeyPath` |

Os papéis do PostgreSQL só são criados na primeira inicialização do volume. Trocar uma senha no `.env` depois do primeiro `up` faz a API falhar na autenticação com o banco, e o remédio é `docker compose down -v` e uma subida nova. O volume novo também já nasce exigindo `scram-sha-256` no soquete local e em `127.0.0.1` dentro do contêiner, por causa de `POSTGRES_INITDB_ARGS`.

## O que o Compose sobe

O `docker-compose.yml` sobe o ledger inteiro para desenvolvimento: `postgres`, `rabbitmq`, o `migrator` e o `dev-keys` (que rodam uma vez e terminam), a `api` e o `worker`, mais o `otel`, com Grafana e coletor OTLP, só com `--profile observability`. O `k6` existe apenas no `docker-compose.test.yml`, no perfil `load`. Imagem e papel de cada serviço estão em [Implantação: visão geral e ambiente local](../04-modelos-c4/implantacao.md), e as portas em [Execução local](execucao-local.md). Não é a topologia de produção: o PostgreSQL é uma instância só, sem standby, e não há TLS, porque as portas ficam em `127.0.0.1`.

As imagens `ledger-api` e `ledger-worker` são construídas em vários estágios. O de compilação parte de `mcr.microsoft.com/dotnet/sdk:10.0`, restaura só com os `.csproj` copiados antes, para o cache de camadas sobreviver a mudanças de código, e compila com `-warnaserror`. O final usa `mcr.microsoft.com/dotnet/aspnet:10.0` (o Worker também, porque abre um servidor web mínimo para os health checks), roda com o usuário sem privilégio da imagem e escolhe a porta por `ASPNETCORE_HTTP_PORTS`. As imagens base dos Dockerfiles, a do RabbitMQ e a do `dev-keys` são fixadas por resumo criptográfico, e o `CiPipelineTests` confere as dos Dockerfiles e a do RabbitMQ. As imagens do ledger levam a etiqueta de `LEDGER_IMAGE_TAG` (padrão `local`), a mesma para todas: para rodar duas versões lado a lado, uma etiqueta para cada.

A sonda de saúde da API e do Worker é o `healthcheck` instalado nas imagens, escrito em `bash`: abre um soquete em `/dev/tcp` e confere o `200` do `/health/ready`, porque as imagens `aspnet` não trazem `curl` nem `wget` e instalar um deles aumentaria a superfície do contêiner. A do PostgreSQL só abre a conexão quando o `postmaster.pid` diz `ready`, e por isso não esbarra no banco ao subir nem ao parar. O `migrator` desliga a sonda, porque termina. Os `depends_on` têm condição: o `postgres` precisa estar saudável para o `migrator` rodar, a API espera o `migrator` e o `dev-keys` terminarem com sucesso, e o Worker espera também o RabbitMQ. API e Worker têm `stop_grace_period` de 35 segundos, maior que o prazo de desligamento do Worker (30).

O `docker-compose.test.yml` é uma camada sobre o arquivo principal. Troca o nome do projeto para `<COMPOSE_PROJECT_NAME>-test` (volumes e rede próprios, mas as mesmas portas no host), desliga o reinício automático para que um contêiner derrubado de propósito continue derrubado, baixa o log para `Warning` e desliga a amostragem de rastros (`OTEL_TRACES_SAMPLER=always_off`). Eleva os limites de taxa: a cota por conta vai a 500 de capacidade e 250 por segundo, e as por chamador ficam bem acima, porque um único `client_id` de teste empurra o volume que em produção viria de vários. Põe ainda a conferência de integridade a cada minuto (`Integrity__RecentIntervalMinutes=1`) e o limiar de atraso do outbox em 5 segundos (`Resilience__Health__OutboxLagSeconds=5`).

## Variáveis do ambiente local

Tudo o que o Compose lê vem do `.env`, que o `.gitignore` deixa de fora. O `.env.example` traz valores de desenvolvimento óbvios, sem segredo real.

| Variável | Padrão | Efeito |
|---|---|---|
| `POSTGRES_PORT`, `RABBITMQ_PORT`, `RABBITMQ_MANAGEMENT_PORT`, `API_PORT`, `WORKER_HEALTH_PORT`, `OTEL_GRAFANA_PORT` | 5432, 5672, 15672, 8080, 8081 e 3000 | Portas publicadas em `127.0.0.1`. A variável de ambiente vale mais que o `.env` |
| `COMPOSE_PROJECT_NAME` | `ledger` | Nome do projeto do Compose. Letras minúsculas, dígitos, hífen e sublinhado, começando por letra ou dígito |
| `LEDGER_IMAGE_TAG` | `local` | Etiqueta das imagens do ledger |
| `LEDGER_LOG_LEVEL` | `Information` | Nível mínimo de log da API e do Worker. Com `Debug`, aparecem cada requisição, as verificações de saúde da API e os ciclos do outbox |
| `RABBITMQ_LOG_LEVEL` | `error` | Nível de log do broker. Com `info`, volta o relato completo, com mais de 200 linhas só na subida |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Vazio | Destino OTLP. Para o coletor do Compose, `http://otel:4317` com `--profile observability` |

## Logs sem ruído

Num ambiente saudável, `docker compose logs` não traz linha `Warning`, `Error` ou `Fatal` (nem `WARNING` e `ERROR` do PostgreSQL), e uma linha dessas é defeito a investigar. O que aparece e não é do ledger vem das imagens oficiais, só na primeira subida de cada volume: o relato do `initdb` e da criação dos papéis, no PostgreSQL, e o banner do RabbitMQ. Os avisos deliberados do ledger são dois: a chave de idempotência reaproveitada com outro corpo (evento 1003, erro do chamador) e a resposta acima de 150 ms que não seja a primeira da rota.

O log do PostgreSQL também fica sem `ERROR` quando o chamador erra o pedido. A escrita em conta inexistente responde 404 `ACCOUNT_NOT_FOUND` sem violar restrição alguma, porque a reserva da chave só insere a linha quando a conta existe (`ReserveKeySql`, em [Fluxo: registro de lançamento](../06-fluxos/registro-de-lancamento.md)). Duas situações de concorrência ainda produzem um `ERROR` do banco, e nenhuma pede ação na primeira ocorrência. Dois estornos simultâneos do mesmo lançamento fazem o segundo violar `uq_ledger_entries_reverses_entry_id` (SQLSTATE 23505), e a API responde 409 `ENTRY_ALREADY_REVERSED`. A espera pelo lock da conta além do `lock_timeout` (55P03) responde 503 e registra o evento 1007, cuja repetição é sinal para seguir [Latência de escrita alta](../08-resiliencia-e-operacao/runbooks.md). Os filtros que mantêm o resto limpo estão em [Saúde e observabilidade](../08-resiliencia-e-operacao/saude-e-observabilidade.md).

## A migração como etapa de implantação

A API nunca migra o esquema ao subir. A migração é uma etapa da implantação, executada antes de qualquer instância nova receber tráfego, pelo comando `Ledger.Worker --migrate`, com o papel `ledger_migrator` e a seção `Postgres:Sources:Migrator`. No Compose, o serviço `migrator` a faz e termina antes de a API e o Worker começarem. O DbUp registra o que já rodou em `schemaversions`, então repetir é seguro, e o comando toma uma trava consultiva (chave 727001) e espera por ela até `Migrations:LockTimeoutSeconds` (120), de modo que duas execuções simultâneas não se atropelam. Os scripts de migração ficam em `src/Ledger.Infrastructure/Persistence/Migrations/`, e o `migrations.sha256` guarda a soma de cada um ([Fluxo: migração do esquema](../06-fluxos/migracao-do-esquema.md)).

| Código de saída de `--migrate` | Significa |
|---|---|
| 0 | Migrações aplicadas, ou nada a aplicar |
| 1 | Um script falhou. A transação do script foi desfeita, e a linha de erro traz o motivo do banco |
| 2 | O banco não foi alcançado (rede, nome, senha ou TLS), ou a trava ficou ocupada além de `Migrations:LockTimeoutSeconds` |
| 3 | Configuração inválida. A linha `Fatal` nomeia cada chave e a regra quebrada |

Uma implantação em que o esquema ficou atrás do código não recebe tráfego: o `/health/ready` da API e o do Worker devolvem 503 enquanto a versão do esquema for menor que a esperada (`SchemaVersionHealthCheck`), e o `/health/live` continua em 200 (`ApiReadinessWithDatabaseTests` e `WorkerHealthSignalsTests`). A ordem é migrar, subir ou renovar a API e o Worker e só então liberar o tráfego, e uma migração nunca é editada depois de aplicada.

## Pilha tecnológica

As versões vêm de `global.json` (SDK) e de `Directory.Packages.props` (pacotes, com versão centralizada).

| Componente | Versão |
|---|---|
| SDK do .NET | 10.0.100, com avanço permitido de versão de recurso (`global.json`) |
| Linguagem | C# 14 |
| PostgreSQL | 16 |
| RabbitMQ | 3.13 |
| Npgsql, Dapper, DbUp, Polly | 10.0.3, 2.1.89, 7.0.1 (`dbup-postgresql`), 8.8.0 (`Polly.Core`) |
| Cliente do RabbitMQ | `RabbitMQ.Client` 7.2.2 |
| Observabilidade | Serilog 10, OpenTelemetry 1.19 |
| Testes | xUnit 2.9.3, Shouldly 4.3.0, NSubstitute 6.2.0, Testcontainers 4.15.0, NetArchTest 1.3.2, coverlet.msbuild 10.1.0 |
