# Configuração e linha de comando

A API e o Worker são configurados por seções tipadas, ligadas a classes de opções e validadas na subida. A página lista cada chave com o padrão, a regra do validador e o efeito, e diz o que cada ambiente recusa e o que significa cada código de saída. Os serviços do Compose e as variáveis de cada um estão em [Ambientes e configuração](../10-implantacao-e-entrega/ambientes-e-configuracao.md).

## Precedência

A configuração vem de quatro fontes, da menos para a mais forte:

1. `appsettings.json`, ao lado de cada executável, com os padrões de todos os ambientes.
2. `appsettings.{Ambiente}.json`, com o que muda no ambiente. O ambiente é o valor de `ASPNETCORE_ENVIRONMENT`.
3. Variáveis de ambiente. O nome é o caminho da chave com dois sublinhados no lugar dos dois-pontos: `Postgres:Sources:Write:Password` vira `Postgres__Sources__Write__Password`.
4. Argumentos de linha de comando no formato do .NET (`--urls`, `--environment`).

Cada executável tem o seu `appsettings.json` e valida só o que usa. A API valida as fontes de banco `Write`, `Balance` e `Statement` e as seções de taxa, autenticação, segurança e `Ledger`. O Worker valida a fonte `Worker`, `RabbitMq`, `Outbox`, `Idempotency`, `Integrity`, `Worker` e as chaves de dados pessoais. O `--migrate` valida a fonte `Migrator` e `Migrations`. O endereço de escuta não é opção do ledger: nas imagens vem de `ASPNETCORE_HTTP_PORTS` (8080 na API, 8081 no Worker), e fora delas serve `ASPNETCORE_URLS`.

## Ambientes

Só `Development` e `Testing` são ambientes permissivos. Qualquer outro nome de `ASPNETCORE_ENVIRONMENT` (`Production`, `Staging`, um erro de digitação, a variável vazia) cai nas regras de produção, e a subida recusa o que a tabela marca.

| Item | `Development` e `Testing` | Qualquer outro nome |
|---|---|---|
| `Postgres:SslMode` | Qualquer valor. O padrão é `Disable` | Só `VerifyFull` |
| `Postgres:IncludeErrorDetail` | Aceita `true` | Só `false` |
| `Authentication:Mode` | `Authority` ou `LocalKey` | Só `Authority`. `LocalKey` é recusado |
| `Authentication:Authority` e `RequireHttpsMetadata` | Livres | Autoridade em `https` e `RequireHttpsMetadata` verdadeiro |
| `Authentication:MaxTokenLifetimeMinutes` | De 1 a 1440 | De 1 a 60 |
| `Authorization:AccountProvisioningClients` | Aceita `*` | Obrigatória, sem `*` |
| `RateLimiting:Enabled` e `ReplenishmentSeconds` | Livres | `Enabled` verdadeiro e `ReplenishmentSeconds` igual a 1 |
| `RabbitMq:UseTls` | Livre | Verdadeiro |
| `Security:Pii:Provider` | `Configuration` ou `Vault` | Só `Vault`. O Worker e a API não iniciam com `Configuration` |
| Rota `GET /openapi/v1.json` | Servida sem token | Não existe |
| Cabeçalho `Strict-Transport-Security` | Ausente em `Development` e presente em `Testing` | Presente |

## Validação na subida

Cada seção é ligada com `ValidateOnStart`: valor ausente onde é obrigatório, fora da faixa ou contra uma regra do validador impede o processo de iniciar. A mensagem nomeia a chave e a regra, por exemplo `Postgres:Sources:Write: The field MaxPoolSize must be between 1 and 500.`, e nunca o valor de um campo sensível. API e Worker escrevem uma linha `Fatal` com todas as falhas, separadas por ponto e vírgula, e saem com o código 3, sem a pilha de exceção. É o evento 5040 (`ApiConfigurationInvalid`) na API e o 5021 no Worker. Esta é a linha da API iniciada sem `Security:Cursor:SigningKey`, aqui sem os campos `Service`, `Version` e `Environment` e sem o identificador de mensagem:

```json
{"@t":"2026-10-05T14:05:36.2510445Z","@m":"Invalid configuration for the API: \"Security:Cursor:SigningKey is required and must be the base64 of exactly 32 bytes.\"","@l":"Fatal","Failures":"Security:Cursor:SigningKey is required and must be the base64 of exactly 32 bytes.","EventId":{"Id":5040,"Name":"ApiConfigurationInvalid"}}
```

## Linha de comando e códigos de saída

| Executável | Argumento | Efeito |
|---|---|---|
| `Ledger.Api` | Nenhum | Sobe o servidor HTTP |
| `Ledger.Worker` | Nenhum | Sobe os laços de fundo e as rotas de saúde |
| `Ledger.Worker` | `--migrate` | Aplica as migrações e sai, sem subir laço de fundo nem rota de saúde |
| `Ledger.Worker` | `--inspect-account=<guid>` | Roda a conferência de integridade de uma conta, registra os achados e sai |

O `--migrate` é exato, e o `--inspect-account` exige o identificador no formato `--inspect-account=<guid>`. Na API, argumentos seguem para a configuração padrão do .NET.

| Código | API e Worker em operação | `--migrate` | `--inspect-account` |
|---|---|---|---|
| 0 | Encerramento normal | Esquema na versão do código: scripts aplicados, ou nenhum a aplicar | Conta sem achados |
| 1 | | Um script falhou, e o que ele fez foi desfeito | A conta tem achados, um por linha de log |
| 2 | | Banco inalcançável, ou a trava de migração ocupada além de `Migrations:LockTimeoutSeconds` | |
| 3 | Configuração inválida | Configuração inválida | Identificador ausente ou inválido, ou configuração inválida |

Os eventos de log da migração são 5010 (início), 5011 (resultado, em `Error` quando o status não é `Succeeded`), 5012 (banco inalcançável, com o motivo na frase), 5013 (trava ocupada além do prazo), 5014 (trava não solta explicitamente), 5015 (detalhe técnico da falha de conexão, em `Debug`) e 5020 (configuração inválida). Os da inspeção são 4006 e 4007 (resultado e achado), 5022 (identificador inválido) e 5023 (configuração inválida).

## Banco de dados: `Postgres`

Lida pela API (fontes `Write`, `Balance` e `Statement`), pelo Worker (fonte `Worker`) e pelo `--migrate` (fonte `Migrator`).

| Chave | Padrão | Regra | Efeito |
|---|---|---|---|
| `Postgres:Host` | `localhost` | Obrigatória | Servidor do PostgreSQL |
| `Postgres:Port` | 5432 | 1 a 65535 | Porta |
| `Postgres:Database` | `ledger` | Obrigatória | Nome do banco |
| `Postgres:SslMode` | `Disable` | Enumeração do Npgsql. Em produção, só `VerifyFull` | Modo de TLS da conexão |
| `Postgres:IncludeErrorDetail` | `false` | Em produção, só `false` | Inclui o detalhe do erro do servidor nas exceções. Nunca em produção, porque o detalhe pode conter valores |
| `Postgres:Sources:<fonte>:Username` | por fonte, abaixo | Obrigatória | Papel do banco |
| `Postgres:Sources:<fonte>:Password` | sem padrão | Obrigatória. Segredo | Senha do papel |
| `Postgres:Sources:<fonte>:MaxPoolSize` | por fonte | 1 a 500 | Tamanho máximo do pool |
| `Postgres:Sources:<fonte>:MinPoolSize` | por fonte | 0 a 500, e não maior que `MaxPoolSize` | Conexões mantidas abertas |
| `Postgres:Sources:<fonte>:ConnectionTimeoutSeconds` | por fonte | 1 a 60 | Espera por uma conexão do pool |
| `Postgres:Sources:<fonte>:CommandTimeoutSeconds` | por fonte | 1 a 3600 | Tempo máximo do cliente por comando |
| `Postgres:Sources:<fonte>:LockTimeoutMs` | ausente | 1 a 600000. Opcional | Quando presente, vira `lock_timeout` da sessão |
| `Postgres:Sources:<fonte>:StatementTimeoutMs` | por fonte | 1 a 3600000 | `statement_timeout` da sessão |
| `Postgres:Sources:<fonte>:IdleInTransactionTimeoutMs` | por fonte | 1 a 3600000 | `idle_in_transaction_session_timeout` da sessão |
| `Postgres:Sources:<fonte>:ReadOnly` | `false` | | Abre a sessão com `default_transaction_read_only=on` |

Valores por fonte nos `appsettings.json`:

| Fonte | Usada por | `Username` | `MaxPoolSize` | `MinPoolSize` | Conexão (s) | Comando (s) | `LockTimeoutMs` | `StatementTimeoutMs` | `IdleInTransactionTimeoutMs` | `ReadOnly` |
|---|---|---|---|---|---|---|---|---|---|---|
| `Write` | API: escrita | `ledger_api` | 7 | 2 | 1 | 2 | 1000 | 2500 | 5000 | `false` |
| `Balance` | API: saldo | `ledger_api` | 8 | 2 | 1 | 1 | ausente | 1500 | 5000 | `true` |
| `Statement` | API: extrato | `ledger_api` | 4 | 1 | 1 | 1 | ausente | 1500 | 5000 | `true` |
| `Worker` | Worker | `ledger_worker` | 5 | 1 | 5 | 10 | 1000 | 10000 | 15000 | `false` |
| `Migrator` | `--migrate` | `ledger_migrator` | 2 | 0 | 5 | 300 | ausente | 300000 | 300000 | `false` |

A cadeia de conexão tem ainda valores fixos no código: `Multiplexing` desligado, `TcpKeepAlive` ligado, vida ociosa de conexão de 300 segundos, poda do pool a cada 10 segundos, até 20 comandos preparados automaticamente a partir da segunda execução, criptografia GSS desligada, `timezone=UTC` em toda sessão e `application_name` por fonte (`ledger-api-write`, `ledger-api-balance`, `ledger-api-statement`, `ledger-worker`, `ledger-migrator`). O uso de cada fonte e o orçamento de conexões estão em [Políticas de resiliência](../08-resiliencia-e-operacao/politicas-de-resiliencia.md).

### Migrações: `Migrations`

| Chave | Padrão | Regra | Efeito |
|---|---|---|---|
| `Migrations:LockTimeoutSeconds` | 120 | 1 a 3600 | Prazo da espera pela trava consultiva da migração, tentada a cada 250 ms. Estourado, o `--migrate` sai com o código 2 |

## Resiliência: `Resilience`

| Chave | Padrão | Regra | Efeito | Quem lê |
|---|---|---|---|---|
| `Resilience:RequestTimeoutSeconds` | 3 | 1 a 60 | Prazo da requisição HTTP. Estourado, responde 503 `SERVICE_UNAVAILABLE` | API |
| `Resilience:ShutdownTimeoutSeconds` | 30 | 1 a 300 | Prazo do desligamento ordenado do processo | API e Worker |
| `Resilience:ServiceUnavailableRetryAfterSeconds` | 1 | 1 a 60 | Valor de `Retry-After` nos 503 | API |
| `Resilience:Health:ProbeTimeoutSeconds` | 1 | 1 a 30 | Prazo de cada sonda de readiness | API e Worker |
| `Resilience:Health:CacheSeconds` | 5 | 1 a 60 | Tempo em que o resultado de uma sonda é reaproveitado | API e Worker |
| `Resilience:Health:RetryAfterSeconds` | 5 | 1 a 60 | Valor de `Retry-After` quando a readiness é 503 | API e Worker |
| `Resilience:Health:OutboxHeartbeatSeconds` | 120 | 10 a 3600 | Silêncio máximo do laço do outbox antes de a liveness do Worker falhar | Worker |
| `Resilience:Health:IntegrityHeartbeatMinutes` | 30 | 1 a 1440 | O mesmo, para os laços da conferência de integridade, recente e completa | Worker |
| `Resilience:Health:OutboxLagSeconds` | 60 | 5 a 3600 | Idade da mensagem pendente mais antiga acima da qual a readiness do Worker passa a `Degraded` | Worker |
| `Resilience:Retry:MaxRetryAttempts` | 2 | 0 a 5 | Repetições da transação de escrita nas falhas transitórias do banco | API |
| `Resilience:Retry:BaseDelayMs` | 50 | 10 a 1000 | Base do recuo exponencial com variação entre as repetições | API |
| `Resilience:BrokerCircuitBreaker:FailureRatio` | 0,5 | 0,1 a 1,0 | Razão de falha que abre o circuito do broker | Worker |
| `Resilience:BrokerCircuitBreaker:SamplingSeconds` | 30 | 5 a 300 | Janela de amostragem | Worker |
| `Resilience:BrokerCircuitBreaker:MinimumThroughput` | 10 | 2 a 1000 | Mínimo de mensagens na janela para o circuito poder abrir | Worker |
| `Resilience:BrokerCircuitBreaker:BreakSeconds` | 30 | 5 a 600 | Tempo aberto antes de testar de novo | Worker |

## Autenticação e autorização: `Authentication`, `Authorization` e `Security:ForwardedHeaders`

Lidas pela API. A validação do token e os escopos estão em [Autenticação e autorização](../07-consistencia-e-seguranca/autenticacao-e-autorizacao.md).

| Chave | Padrão | Regra | Efeito |
|---|---|---|---|
| `Authentication:Mode` | `Authority` | `Authority` ou `LocalKey`. Em produção, só `Authority` | De onde vem a chave pública que valida a assinatura |
| `Authentication:Authority` | `https://auth.bank.internal` | URI absoluta no modo `Authority`. Em produção, `https` | Emissor consultado para obter as chaves |
| `Authentication:Issuer` | `https://auth.bank.internal` | Obrigatória | Valor exigido na claim `iss` |
| `Authentication:Audience` | `ledger-api` | Obrigatória | Valor exigido na claim `aud` |
| `Authentication:RequireHttpsMetadata` | `true` | Em produção, só `true` | Exige `https` na obtenção dos metadados do emissor |
| `Authentication:MaxTokenLifetimeMinutes` | 15 | 1 a 1440. Em produção, até 60 | Teto da diferença entre emissão e expiração aceita no token |
| `Authentication:RequireAccessTokenType` | `true` | | Exige o tipo `at+jwt` ou `application/at+jwt` no cabeçalho do token |
| `Authentication:LocalKey:PublicKeyPath` | ausente | Arquivo PEM com chave pública RSA de 2048 bits ou mais, ou EC P-256. Não pode conter chave privada | Chave pública do modo `LocalKey`. Só desenvolvimento e teste |
| `Authentication:LocalKey:SigningKey` | ausente | Ao menos 32 caracteres. Segredo | Chave simétrica do modo `LocalKey`, usada só se `PublicKeyPath` estiver ausente |
| `Authorization:AccountProvisioningClients` | vazio (`["*"]` em `Development`) | Sem itens em branco. Em produção, não vazia e sem `*` | Lista dos `client_id` que podem chamar `POST /v1/accounts`. `*` libera todos |
| `Security:ForwardedHeaders:KnownNetworks` | vazio | Redes em notação CIDR, nenhuma `/0` | Redes cujo `X-Forwarded-For` é aceito. Vazia, o tratamento dos cabeçalhos de proxy fica desligado |
| `AllowedHosts` | `*` | Nomes de host separados por ponto e vírgula, ou `*` | Filtro de `Host` do ASP.NET Core, lido pela API: um pedido cujo cabeçalho `Host` não consta da lista é recusado. O padrão aceita qualquer nome |

O `appsettings.Development.json` da API troca o modo para `LocalKey`, aponta `Authentication:LocalKey:PublicKeyPath` para `../../.local/keys/dev-public.pem`, usa o emissor `https://idp.local.test`, desliga `RequireHttpsMetadata`, sobe `MaxTokenLifetimeMinutes` para 240, libera `Authorization:AccountProvisioningClients` com `*` e liga `Postgres:IncludeErrorDetail`. No Compose, a chave pública vem de `DEV_JWT_PUBLIC_KEY_PEM_B64`, no `.env`, e é gravada num volume lido em `/run/keys/dev-public.pem`, que é o `PublicKeyPath` do contêiner. Com `dotnet run`, fora do Compose, o caminho se troca por `Authentication__LocalKey__PublicKeyPath`, apontando para o `dev-keys/public.pem`. O Worker, em `Development`, só liga o `Postgres:IncludeErrorDetail`. Sem opção de configuração, o validador do `JwtBearer` exige assinatura e expiração, tolera 30 segundos de relógio, aceita RS256 e ES256 (HS256 só no modo `LocalKey` com chave simétrica) e mantém `IncludeErrorDetails` falso.

## Dados pessoais, cursor e auditoria: `Security`

| Chave | Padrão | Regra | Efeito | Quem lê |
|---|---|---|---|---|
| `Security:Pii:Provider` | `Configuration` | `Configuration` ou `Vault`. Em produção, só `Vault` | Origem das chaves de dados pessoais | API e Worker |
| `Security:Pii:ActiveKeyVersion` | 1 | 1 a 65535. Precisa existir entre os conjuntos de chaves | Versão usada para cifrar | API e Worker |
| `Security:Pii:KeySets:<versão>:EncryptionKey` | sem padrão | Base64 de 32 bytes, diferente da `BlindIndexKey`. Segredo | Chave AES-256-GCM da versão. Só no provedor `Configuration` | API e Worker |
| `Security:Pii:KeySets:<versão>:BlindIndexKey` | sem padrão | Base64 de 32 bytes. Segredo | Chave do HMAC do índice cego da versão. Só no provedor `Configuration` | API e Worker |
| `Security:Pii:Directory` | ausente | Obrigatória no provedor `Vault` | Pasta com uma subpasta por versão (`1`, `2`), cada uma com os arquivos `encryption.key` e `blind-index.key` | API e Worker |
| `Security:Pii:ReloadMinutes` | 10 | 1 a 1440 | Período de recarga da pasta de chaves no provedor `Vault` | API e Worker |
| `Security:Pii:Rewrap:BatchSize` | 500 | 1 a 5000 | Contas recifradas por lote | Worker |
| `Security:Pii:Rewrap:IdleSeconds` | 60 | 5 a 3600 | Espera do laço de recifragem quando não há o que fazer | Worker |
| `Security:Cursor:SigningKey` | sem padrão | Obrigatória na API. Base64 de exatamente 32 bytes. Segredo | Chave de assinatura do cursor do extrato | API |
| `Security:Audit:DeniedWrite:Capacity` | 10 | 1 a 1000 | Teto de gravação da trilha para escritas negadas: capacidade do balde | API |
| `Security:Audit:DeniedWrite:RefillPerSecond` | 1 | 1 a 1000 | Reposição por segundo do mesmo balde | API |

O provedor `Vault` lê as chaves de uma pasta de segredos e não fala com um cofre externo: nenhum adaptador para cofre foi escrito, e o comportamento com o cofre fora do ar só foi exercitado em teste, nunca contra um cofre real ([limites conhecidos](../09-qualidade/limites-conhecidos.md)). As versões de chave, a rotação e a pasta indisponível estão em [Proteção de dados](../07-consistencia-e-seguranca/protecao-de-dados.md).

## Limites de taxa e de concorrência: `RateLimiting`

Lida pela API. A cadeia de limites, a ordem em que agem e a resposta de cada um estão em [Limites de taxa e de concorrência](../08-resiliencia-e-operacao/limites-de-taxa-e-concorrencia.md).

| Chave | Padrão | Regra | Efeito |
|---|---|---|---|
| `RateLimiting:Enabled` | `true` | Em produção, só `true` | Liga a cadeia de limites |
| `RateLimiting:ReplenishmentSeconds` | 1 | 1 a 3600. Em produção, só 1 | Período de reposição dos baldes de fichas |
| `RateLimiting:WritePerClient:Capacity` | 3000 | 1 a 1000000 | Capacidade do balde de escrita por chamador |
| `RateLimiting:WritePerClient:RefillPerSecond` | 1500 | 1 a 1000000, e não maior que a capacidade | Reposição por segundo do mesmo balde |
| `RateLimiting:ReadPerClient:Capacity` | 12000 | 1 a 1000000 | Capacidade do balde de leitura por chamador |
| `RateLimiting:ReadPerClient:RefillPerSecond` | 8000 | 1 a 1000000, e não maior que a capacidade | Reposição por segundo |
| `RateLimiting:WritePerAccount:Capacity` | 100 | 1 a 1000000 | Capacidade do balde de escrita por conta |
| `RateLimiting:WritePerAccount:RefillPerSecond` | 50 | 1 a 1000000, e não maior que a capacidade | Reposição por segundo |
| `RateLimiting:WriteConcurrency` | 16 | 1 a 256, entre 1 e 3 vezes `Postgres:Sources:Write:MaxPoolSize` | Requisições de escrita em voo ao mesmo tempo |
| `RateLimiting:BalanceConcurrency` | 16 | 1 a 256, entre 1 e 3 vezes `Postgres:Sources:Balance:MaxPoolSize` | Consultas de saldo em voo |
| `RateLimiting:StatementConcurrency` | 8 | 1 a 256, entre 1 e 3 vezes `Postgres:Sources:Statement:MaxPoolSize` | Consultas de extrato em voo |

Nas classes de opções os padrões são menores (100 de capacidade e 50 de reposição), e os valores acima são os do `appsettings.json` da API.

## Regras de negócio da API: `Ledger`

| Chave | Padrão | Regra | Efeito |
|---|---|---|---|
| `Ledger:OccurredAtFutureToleranceMinutes` | 5 | 0 a 60 | Quanto o `occurredAt` de um lançamento pode passar do relógio da API |
| `Ledger:Balance:SettlingWindowSeconds` | 5 | 1 a 60 | Janela de acomodação: um `asOf` mais antigo que ela, contada do relógio do banco, devolve `settled` verdadeiro |
| `Ledger:Statement:DefaultLimit` | 50 | 1 a 200, e não maior que `MaxLimit` | Tamanho da página quando `limit` não é enviado |
| `Ledger:Statement:MaxLimit` | 200 | 1 a 200 | Maior `limit` aceito no extrato |

## Mensageria e rotinas do Worker

Lidas pelo Worker. A publicação do outbox e as rotinas estão em [Fluxo: publicação do outbox](../06-fluxos/publicacao-do-outbox.md) e [Fluxo: rotinas de manutenção do Worker](../06-fluxos/rotinas-de-manutencao.md).

| Chave | Padrão | Regra | Efeito |
|---|---|---|---|
| `RabbitMq:Host` | `localhost` | Obrigatória | Servidor do broker |
| `RabbitMq:Port` | 5672 | 1 a 65535 | Porta AMQP |
| `RabbitMq:VirtualHost` | `/` | Obrigatória | Virtual host |
| `RabbitMq:Username` | `ledger_worker` | Obrigatória | Usuário do broker |
| `RabbitMq:Password` | sem padrão | Obrigatória. Segredo | Senha do usuário |
| `RabbitMq:UseTls` | `false` | Em produção, só `true` | TLS na conexão |
| `RabbitMq:ConnectTimeoutSeconds` | 2 | 1 a 60 | Prazo de abertura da conexão |
| `RabbitMq:Exchange` | `ledger.events` | Obrigatória | Nome da troca de eventos |
| `RabbitMq:ReconnectMinSeconds` | 1 | 1 a 300, e não maior que o máximo | Primeira espera entre tentativas de conexão |
| `RabbitMq:ReconnectMaxSeconds` | 30 | 1 a 300 | Maior espera. O recuo dobra a cada falha |
| `RabbitMq:ReconnectJitterPercent` | 20 | 0 a 50 | Variação aleatória de cada espera, para mais ou para menos |
| `RabbitMq:Retention:Enabled` | `true` | | Declara a fila de retenção. Desligada, o Worker declara só a troca |
| `RabbitMq:Retention:Queue` | `retention.ledger.entry-registered` | 3 a 200 caracteres, sem o prefixo `amq.` | Nome da fila. As de mensagens mortas derivam dele (`.dlx` e `.dead-letter`) |
| `RabbitMq:Retention:TtlHours` | 24 | 1 a 720 | Vida de uma mensagem na fila de retenção |
| `RabbitMq:Retention:MaxLength` | 1000000 | 1000 a 100000000 | Mensagens guardadas, ao custo da mais antiga (`drop-head`) |
| `RabbitMq:Retention:MaxMegabytes` | 1024 | 1 a 102400 | Tamanho máximo da fila, em MiB |
| `RabbitMq:Retention:DeadLetterTtlHours` | 168 | 1 a 720 | Vida das mensagens na fila de mortas |
| `RabbitMq:Retention:DeadLetterMaxLength` | 100000 | 1000 a 10000000 | Mensagens guardadas na fila de mortas |
| `Outbox:BatchSize` | 200 | 1 a 1000 | Mensagens reivindicadas por volta |
| `Outbox:IdlePollMs` | 200 | 10 a 5000 | Espera quando o lote vem incompleto ou vazio |
| `Outbox:LeaseSeconds` | 30 | 5 a 600, e maior que `ConfirmTimeoutSeconds` | Prazo da reivindicação de um lote |
| `Outbox:ConfirmTimeoutSeconds` | 5 | 1 a 60 | Prazo das confirmações do broker para um lote |
| `Outbox:RetentionDays` | 7 | 1 a 365 | Idade a partir da qual uma mensagem publicada é podada |
| `Outbox:PruneIntervalMinutes` | 10 | 1 a 1440 | Período da poda do outbox |
| `Outbox:PruneBatchSize` | 5000 | 100 a 50000 | Linhas removidas por lote da poda |
| `Outbox:MeasureIntervalSeconds` | 10 | 1 a 300 | Período da medição do acúmulo |
| `Outbox:PendingCap` | 1000000 | 1000 a 10000000 | Teto da contagem de pendentes na medição |
| `Outbox:FailedAttempts` | 5 | 2 a 100 | Tentativas a partir das quais uma mensagem conta como emperrada |
| `Outbox:FailedHeadWindow` | 1000 | 10 a 10000 | Quantas pendentes, das mais antigas, entram na contagem de emperradas |
| `Idempotency:RetentionDays` | 35 | 1 a 365 | Idade a partir da qual as chaves de idempotência são podadas |
| `Idempotency:PruneIntervalMinutes` | 10 | 1 a 1440 | Período da poda das chaves |
| `Idempotency:PruneBatchSize` | 5000 | 100 a 50000 | Linhas removidas por lote da poda |
| `Integrity:RecentIntervalMinutes` | 5 | 1 a 60 | Período da conferência de integridade recente |
| `Integrity:RecentOverlapMinutes` | 1 | 1 a 60 | Sobreposição da janela recente com a anterior |
| `Integrity:FullIntervalHours` | 24 | 1 a 168 | Período da conferência completa |
| `Integrity:HeadBatchSize` | 5000 | 100 a 20000 | Contas por lote na conferência dos saldos atuais |
| `Integrity:ChainSliceMinutes` | 10 | 1 a 60 | Tamanho de cada fatia de tempo na conferência do encadeamento |
| `Worker:FailureBackoff:MinSeconds` | 1 | 1 a 60, e não maior que o máximo | Primeira espera de um laço do Worker depois de uma falha |
| `Worker:FailureBackoff:MaxSeconds` | 30 | 1 a 60 | Maior espera, com recuo exponencial |

## Logs e telemetria

A seção `Serilog` segue o formato do Serilog. Os `appsettings.json` usam `Information` como nível padrão, com as substituições abaixo.

| Origem | API | Worker |
|---|---|---|
| `Microsoft` | | `Warning` |
| `Microsoft.AspNetCore` | `Warning` | |
| `Microsoft.Extensions.Diagnostics.HealthChecks` | `Warning` | |
| `Npgsql` | `Warning` | `Warning` |
| `Ledger.Audit` | `Information` | |

O Compose ajusta o nível padrão por `LEDGER_LOG_LEVEL`, que alimenta `Serilog__MinimumLevel__Default`, e os logs saem em JSON na saída padrão. Traces e métricas são ligados pelas variáveis padrão do OpenTelemetry:

| Variável | Efeito |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Endpoint OTLP geral. Com valor, liga o envio de traces e de métricas |
| `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT`, `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` | Endpoint específico de cada sinal |
| `OTEL_TRACES_EXPORTER`, `OTEL_METRICS_EXPORTER` | `none` desliga o envio do sinal |
| `OTEL_SERVICE_NAME` | Nome do serviço nos dados exportados. Padrão: `ledger-api` e `ledger-worker` |

Sem endpoint nada é enviado, e o ledger funciona do mesmo jeito. As métricas estão no [Catálogo de métricas](../08-resiliencia-e-operacao/catalogo-de-metricas.md).

## Segredos

Não aparecem nos arquivos: `Postgres:Sources:<fonte>:Password`, `RabbitMq:Password`, `Security:Pii:KeySets:<versão>:EncryptionKey` e `BlindIndexKey`, `Security:Cursor:SigningKey` e a chave do modo `LocalKey`. Em desenvolvimento vêm do `.env`, copiado de `.env.example`, que traz só senhas e chaves de desenvolvimento, e são levados às chaves desta página pelo Compose, que define ainda `ASPNETCORE_ENVIRONMENT=Development`, o host `postgres`, o broker `rabbitmq` e `Postgres__SslMode=Disable`. A tabela de variáveis está em [Ambientes e configuração](../10-implantacao-e-entrega/ambientes-e-configuracao.md). Em produção, devem vir do cofre da plataforma.
