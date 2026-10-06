workspace "Ledger Bancário" "Ledger de um banco digital: registra créditos e débitos nas contas e responde o saldo em qualquer instante, sem perder nem duplicar dinheiro." {

    model {
        plantao = person "Plantão da plataforma" "Implanta, migra o esquema, investiga incidentes e atende os alertas." "Pessoa"

        canais = softwareSystem "Canais e meios de pagamento" "App do banco, Pix e cartões. Lançam créditos e débitos e consultam o saldo." "Sistema externo"
        conciliacao = softwareSystem "Conciliação e back-office" "Leem extratos e saldos históricos e pedem estornos." "Sistema externo"
        idp = softwareSystem "Emissor de tokens" "Provedor de identidade do banco. Emite JWT por client credentials e publica as chaves públicas." "Sistema externo"
        cofre = softwareSystem "Cofre de chaves" "Fornece as chaves de dados pessoais como arquivos de uma pasta de segredos montada na API e no Worker. Não existe adaptador para um cofre externo." "Sistema externo"
        telemetria = softwareSystem "Plataforma de observabilidade" "Recebe traces e métricas por OTLP, coleta os logs do stdout e mantém painéis e alertas." "Sistema externo"
        consumidores = softwareSystem "Consumidores de eventos" "Notificação, antifraude e analytics. Assinam EntryRegistered e deduplicam pelo message_id." "Sistema externo"

        ledger = softwareSystem "Ledger" "Registra créditos e débitos, responde o saldo em qualquer instante e publica um evento por lançamento." "Sistema em foco" {

            api = container "Ledger.Api" "Valida token e escopo, aplica limites e executa os casos de uso de conta, lançamento, estorno, saldo e extrato. Sem estado, porta 8080." "ASP.NET Core 10, Minimal API" {

                group "Cadeia HTTP" {
                    forwardedHeaders = component "ForwardedHeaders" "Restaura o endereço e o esquema do cliente. Só entra na cadeia quando Security:ForwardedHeaders:KnownNetworks tem valor." "Middleware ASP.NET Core" "Cadeia HTTP"
                    correlationIdMiddleware = component "CorrelationIdMiddleware" "Aceita o X-Correlation-Id que casa com o formato, senão gera outro, e o devolve em toda resposta." "Middleware" "Cadeia HTTP"
                    securityHeadersMiddleware = component "SecurityHeadersMiddleware" "Acrescenta nosniff, Content-Security-Policy, Referrer-Policy, Cache-Control no-store e, fora do Development, Strict-Transport-Security." "Middleware" "Cadeia HTTP"
                    requestLogging = component "Log de requisição" "UseSerilogRequestLogging com RequestLogEnricher e ColdStartRequestLogLevel: uma linha por requisição, com o nível decidido pelo status e pela duração." "Serilog" "Cadeia HTTP"
                    exceptionHandling = component "GlobalExceptionHandler" "Converte exceção em Problem Details: falha transitória e prazo estourado viram 503 com Retry-After, o resto vira 500 sem detalhe interno." "IExceptionHandler, ProblemFactory, ProblemCatalog" "Cadeia HTTP"
                    requestTimeouts = component "Prazo da requisição" "ConfigureRequestTimeouts aplica o prazo de Resilience:RequestTimeoutSeconds (3 s por padrão) e o RequestTimeoutMarkerMiddleware marca a requisição que estourou." "Microsoft.AspNetCore.Http.Timeouts" "Cadeia HTTP"
                    authentication = component "Autenticação JWT" "ConfigureJwtBearerOptions e AuthenticationEvents: RS256 ou ES256, tolerância de relógio de 30 s, teto de vida do token e client_id obrigatório. Responde 401." "JwtBearer" "Cadeia HTTP"
                    clientIdActivityMiddleware = component "ClientIdActivityMiddleware" "Copia o client_id do token para o span da requisição." "Middleware" "Cadeia HTTP"
                    rateLimiting = component "Limitadores" "RequestLimiters em cadeia: concorrência por classe de rota (503), cota por chamador e cota por conta (429). RateLimitRejectionWriter escreve a recusa." "Microsoft.AspNetCore.RateLimiting" "Cadeia HTTP"
                    authorization = component "Autorização por escopo" "ScopeAuthorizationHandler, ProvisioningClientHandler e LedgerAuthorizationResultHandler: ledger.read nos GET, ledger.write nos POST, cliente de provisionamento na criação de conta. Responde 403." "Políticas de autorização" "Cadeia HTTP"
                }

                group "Rotas" {
                    accountsEndpoints = component "AccountsEndpoints" "POST /v1/accounts, rota administrativa aberta só aos clientes de provisionamento." "Minimal API" "Rota"
                    entriesEndpoints = component "EntriesEndpoints" "POST /v1/accounts/{accountId}/entries e POST .../entries/{entryId}/reversals, com Idempotency-Key obrigatória." "Minimal API" "Rota"
                    balanceEndpoints = component "BalanceEndpoints" "GET /v1/accounts/{accountId}/balance, com o parâmetro asOf." "Minimal API" "Rota"
                    statementEndpoints = component "StatementEndpoints" "GET /v1/accounts/{accountId}/entries, extrato paginado por cursor." "Minimal API" "Rota"
                    healthEndpoints = component "HealthEndpoints" "GET /health/live e GET /health/ready, anônimos, só GET e HEAD, fora do prazo de requisição." "ASP.NET Core Health Checks" "Rota"
                    openApiEndpoint = component "OpenApiEndpoints" "GET /openapi/v1.json, só em Development e Testing. Os transformadores LedgerDocumentTransformer, LedgerOperationTransformer e LedgerSchemaTransformer montam o documento." "Microsoft.AspNetCore.OpenApi" "Rota"
                    requestReaders = component "Leitores de requisição" "RequestBodyReader, JsonContentType, IdempotencyKeyReader, CreateAccountRequestReader, RegisterEntryRequestReader, ReverseEntryRequestReader, BalanceQueryReader e StatementQueryReader: leitura estrita, todos os erros de uma vez." "Validation" "Rota"
                    apiReadinessChecks = component "Verificações de readiness" "PostgresReadinessChecks (postgres e schema, em cache), KeyProviderHealthCheck (keys) e ShutdownHealthCheck (shutdown)." "IHealthCheck" "Rota"
                }

                group "Aplicação" {
                    createAccountHandler = component "CreateAccountHandler" "Valida moeda e limite, cifra o documento, grava conta, saldo zero e account.created numa transação, com Idempotency-Key opcional." "Handler, Result" "Caso de uso"
                    registerEntryHandler = component "RegisterEntryHandler" "Monta o lançamento e entrega a transação inteira ao IUnitOfWork como uma função." "Handler, Result" "Caso de uso"
                    reverseEntryHandler = component "ReverseEntryHandler" "Lê o original, planeja o estorno e reaproveita o mesmo fluxo de escrita do lançamento." "Handler, Result" "Caso de uso"
                    entryWriteFlow = component "EntryWriteFlow" "A função da transação: reserva a chave, compara o hash na repetição, aplica o saldo, explica a recusa e enfileira o evento." "Classe estática interna" "Caso de uso"
                    getBalanceHandler = component "GetBalanceHandler" "Saldo atual por chave primária e saldo em um instante pelo último lançamento até T, com o campo settled." "Handler, Result" "Caso de uso"
                    listEntriesHandler = component "ListEntriesHandler" "Extrato do mais recente ao mais antigo, uma linha a mais que o limit para saber se há próxima página." "Handler, Result" "Caso de uso"
                    readAudit = component "ReadAudit" "Registra BalanceQueried (2001) e StatementQueried (2002) na categoria de log Ledger.Audit." "LoggerMessage" "Caso de uso"
                    portasEscrita = component "Portas de escrita" "IUnitOfWork, IUnitOfWorkScope, IIdempotencyStore, IEntryRepository, IAccountRepository, IOutbox, IAuditTrail, IHolderDocumentProtector e IIdGenerator." "Interfaces C#" "Porta"
                    portasLeitura = component "Portas de leitura" "IBalanceReader, IStatementReader e IStatementCursorProtector." "Interfaces C#" "Porta"
                }

                group "Domínio" {
                    domainShared = component "Domain.Shared" "Result, Error, ErrorKind, Money e MoneyErrors. Regra de negócio devolve Result, nunca exceção." "C# 14" "Domínio"
                    domainEntries = component "Domain.Entries" "Entry (Credit, Debit, ReversalOf), EntryId, EntryType, IdempotencyKey, ReversalCandidate, ReversalPlan, StatementPosition, StatementBounds, EntryErrors e StatementErrors." "C# 14" "Domínio"
                    domainAccounts = component "Domain.Accounts" "AccountBalance (a regra do piso), AccountId, HolderDocument, HolderDocumentKind, AccountErrors e BalanceErrors." "C# 14" "Domínio"
                }

                group "Infraestrutura" {
                    unitOfWork = component "PostgresUnitOfWork" "Abre a transação em READ COMMITTED na fonte Write e a repete, com PostgresUnitOfWorkScope e WriteRetryPipeline, quando a falha é transitória." "Npgsql, Polly" "Infraestrutura"
                    idempotencyStore = component "PostgresIdempotencyStore" "Reserva (conta, chave) com ON CONFLICT DO NOTHING e lê a repetição para comparar o hash." "Npgsql, Dapper" "Infraestrutura"
                    entryRepository = component "PostgresEntryRepository" "Aplica o saldo e grava o lançamento numa instrução só, e lê o candidato ao estorno." "Npgsql, Dapper" "Infraestrutura"
                    accountRepository = component "PostgresAccountRepository" "Cria conta e saldo, reserva a chave de criação e diagnostica a recusa de um débito." "Npgsql, Dapper" "Infraestrutura"
                    outbox = component "PostgresOutbox" "Insere o EntryRegistered em outbox_messages dentro da transação do lançamento." "Npgsql, Dapper" "Infraestrutura"
                    scopedAudit = component "PostgresScopedAuditTrail" "Grava a linha de audit_log na mesma transação da conta criada." "Npgsql, Dapper" "Infraestrutura"
                    deniedWriteAuditor = component "DeniedWriteAuditor" "Registra em audit_log a escrita negada por escopo, com teto de gravação por cliente, fora do caminho da resposta." "PostgresAuditTrail" "Infraestrutura"
                    balanceReader = component "PostgresBalanceReader" "Lê account_balances pela chave primária e ledger_entries pelo índice de conta e recorded_at, na fonte Balance, somente leitura." "Npgsql, Dapper" "Infraestrutura"
                    statementReader = component "PostgresStatementReader" "Lê a página do extrato pelo índice de conta e recorded_at, na fonte Statement, somente leitura." "Npgsql, Dapper" "Infraestrutura"
                    cursorProtector = component "HmacStatementCursorProtector" "Assina o cursor do extrato com HMAC e o amarra à conta." "HMAC-SHA-256" "Infraestrutura"
                    piiProtector = component "HolderDocumentProtector" "Cifra o documento com AES-256-GCM amarrado ao account_id e calcula o índice cego por HMAC, com AesGcmDocumentCipher e HmacBlindIndex." "AES-GCM, HMAC" "Infraestrutura"
                    keyProvider = component "ReloadingKeyProvider" "Entrega a chave ativa e as versões vivas, lidas por IKeySetSource (DirectoryKeySetSource, uma pasta de segredos) e relidas a cada Security:Pii:ReloadMinutes. ConfigurationKeyProvider em Development e Testing." "IKeyProvider" "Infraestrutura"
                    apiObservability = component "Observability" "Serilog em JSON no stdout, ActivitySource e Meter Ledger, mascaramento de dado sensível e exportação OTLP de traces e métricas." "Serilog, OpenTelemetry" "Infraestrutura"
                }
            }

            worker = container "Ledger.Worker" "Publica o outbox, mede e poda o acúmulo, poda as chaves de idempotência, confere a integridade e recifra documentos. Saúde na porta 8081. Com --migrate aplica as migrações e com --inspect-account confere uma conta." ".NET 10, ASP.NET Core e serviços de fundo" {

                group "Host e comandos" {
                    workerHost = component "WorkerLoopHost e WorkerLoopService" "Base dos laços: abre um escopo por ciclo, aplica recuo exponencial na falha, registra o batimento e conta o ciclo." "BackgroundService" "Serviço de fundo"
                    migrateCommand = component "MigrateCommand" "Com --migrate chama o IDatabaseMigrator e traduz o resultado em código de saída: 0, 1, 2 ou 3." "Comando de linha de comando" "Serviço de fundo"
                    inspectAccountCommand = component "InspectAccountCommand" "Com --inspect-account=guid confere uma conta e sai com 0, 1 ou 3. Não sobe os laços." "Comando de linha de comando" "Serviço de fundo"
                    workerHealth = component "HealthEndpoints do Worker" "GET /health/live pelo batimento dos laços e GET /health/ready pelas verificações marcadas como ready." "ASP.NET Core Health Checks" "Serviço de fundo"
                }

                group "Serviços de fundo" {
                    outboxPublisherService = component "OutboxPublisherService" "Laço de publicação: reivindica lotes, publica com confirmação e marca. Com o circuito aberto não reivindica." "BackgroundService" "Serviço de fundo"
                    outboxPruneService = component "OutboxPruneService" "Poda em lotes as mensagens publicadas há mais de Outbox:RetentionDays." "BackgroundService" "Serviço de fundo"
                    outboxMeasurementService = component "OutboxMeasurementService" "Mede o acúmulo e a idade da mensagem pendente mais antiga a cada Outbox:MeasureIntervalSeconds." "BackgroundService" "Serviço de fundo"
                    idempotencyPruneService = component "IdempotencyPruneService" "Poda em lotes as chaves de idempotência de lançamento e de criação de conta mais antigas que Idempotency:RetentionDays." "BackgroundService" "Serviço de fundo"
                    integrityCheckService = component "IntegrityCheckService" "Dois laços: execução recente e execução completa. Só detecta, registra e alerta, nunca corrige." "BackgroundService" "Serviço de fundo"
                    keyRewrapService = component "KeyRewrapService" "Registra keys.version_activated na subida e recifra, em lotes, os documentos sob versão antiga da chave." "BackgroundService" "Serviço de fundo"
                }

                group "Aplicação" {
                    publishOutboxBatchHandler = component "PublishOutboxBatchHandler" "Conecta, sonda o circuito, reivindica o lote, publica em paralelo e marca o que o broker confirmou." "Handler" "Caso de uso"
                    pruneOutboxHandler = component "PruneOutboxHandler" "Repete a poda em lotes até esvaziar o que passou da retenção." "Handler" "Caso de uso"
                    measureOutboxHandler = component "MeasureOutboxHandler" "Lê o acúmulo e a idade da mais antiga e alimenta métricas e readiness." "Handler" "Caso de uso"
                    pruneIdempotencyKeysHandler = component "PruneIdempotencyKeysHandler" "Repete a poda de chaves de idempotência em lotes." "Handler" "Caso de uso"
                    runIntegrityCheckHandler = component "RunIntegrityCheckHandler" "Percorre contas e cadeia em fatias, classifica cada achado e registra a execução em audit_log." "Handler" "Caso de uso"
                    inspectAccountIntegrityHandler = component "InspectAccountIntegrityHandler" "Confere uma conta sob demanda." "Handler" "Caso de uso"
                    rewrapAccountsHandler = component "RewrapAccountsHandler" "Decifra com a versão gravada e cifra de novo com a ativa, em lotes, sob trava consultiva." "Handler" "Caso de uso"
                    recordKeyActivationHandler = component "RecordKeyActivationHandler" "Grava keys.version_activated uma vez por versão ativa." "Handler" "Caso de uso"
                    portasWorker = component "Portas do Worker" "IOutboxQueue, IEventPublisher, IIdempotencyKeyPruner, IIntegritySessions, IAccountKeyRewrapper, IDatabaseMigrator, IAuditTrail, IHolderDocumentProtector, IWorkerHeartbeat e as portas de telemetria." "Interfaces C#" "Porta"
                }

                group "Infraestrutura" {
                    outboxQueue = component "PostgresOutboxQueue" "Reivindica com FOR UPDATE SKIP LOCKED, marca published_at, devolve as não tentadas, poda e mede." "Npgsql, Dapper" "Infraestrutura"
                    circuitBreakingPublisher = component "CircuitBreakingEventPublisher" "Circuit breaker do Polly em volta da publicação: abre com a proporção de falhas de Resilience:BrokerCircuitBreaker contada por mensagem." "Polly" "Infraestrutura"
                    rabbitMqPublisher = component "RabbitMqEventPublisher e BrokerConnection" "Publica com confirmação e mandatory na exchange ledger.events, declara a topologia a cada conexão e reconecta com recuo exponencial." "RabbitMQ.Client" "Infraestrutura"
                    idempotencyKeyPruner = component "PostgresIdempotencyKeyPruner" "Remove em lotes as chaves vencidas de idempotency_keys e de account_creation_keys." "Npgsql, Dapper" "Infraestrutura"
                    integritySessions = component "PostgresIntegritySessions" "Abre a sessão da execução sob trava consultiva por modo, roda as consultas de topo e de cadeia e grava a execução e os achados." "Npgsql, Dapper" "Infraestrutura"
                    keyRewrapper = component "PostgresAccountKeyRewrapper" "Seleciona lotes com FOR NO KEY UPDATE SKIP LOCKED sob trava consultiva e atualiza as colunas do documento em accounts." "Npgsql, Dapper" "Infraestrutura"
                    migrationRunner = component "MigrationRunner" "Aplica os scripts SQL embutidos em ordem, cada um na sua transação, sob a trava consultiva da migração." "DbUp" "Infraestrutura"
                    wrkAuditTrail = component "PostgresAuditTrail" "Grava as linhas de audit_log do Worker (execuções da conferência, ativação de chave, recifragem)." "Npgsql, Dapper" "Infraestrutura"
                    wrkPiiProtector = component "HolderDocumentProtector (Worker)" "Decifra com a versão antiga e cifra com a ativa, recalculando o índice cego." "AES-GCM, HMAC" "Infraestrutura"
                    wrkKeyProvider = component "ReloadingKeyProvider (Worker)" "Entrega as versões vivas do conjunto de chaves lido da pasta de segredos." "IKeyProvider" "Infraestrutura"
                    workerState = component "Estado de saúde" "WorkerHeartbeat, OutboxStatsHolder e KeyUsageHolder: o que os laços escrevem e as verificações leem." "Singletons" "Infraestrutura"
                    workerReadinessChecks = component "Verificações do Worker" "WorkerLivenessHealthCheck, PostgresReadinessChecks, OutboxLagHealthCheck, KeyUsageHealthCheck, BrokerCircuitHealthCheck, CachedRabbitMqReadiness e ShutdownHealthCheck." "IHealthCheck" "Infraestrutura"
                    wrkObservability = component "Observability (Worker)" "Serilog em JSON no stdout, ActivitySource e Meter Ledger e exportação OTLP de traces e métricas." "Serilog, OpenTelemetry" "Infraestrutura"
                }
            }

            db = container "PostgreSQL 16" "accounts, account_balances, ledger_entries, idempotency_keys, outbox_messages, audit_log e account_creation_keys, mais o diário schemaversions do DbUp. Única fonte de verdade." "Banco relacional" "Banco de dados"

            broker = container "RabbitMQ 3.13" "Exchange ledger.events (topic, durável) e a fila de retenção retention.ledger.entry-registered. Cada consumidor declara a sua fila." "Broker AMQP" "Broker de mensagens"
        }

        canais -> ledger "Lança, estorna e consulta" "HTTPS, JSON, JWT"
        conciliacao -> ledger "Lê extrato e saldo em um instante, pede estorno" "HTTPS, JSON, JWT"
        plantao -> ledger "Implanta, migra o esquema e investiga"
        plantao -> telemetria "Acompanha painéis e alertas" "" "Telemetria"
        telemetria -> plantao "Dispara os alertas"
        ledger -> idp "Baixa as chaves públicas para validar tokens" "HTTPS, JWKS"
        ledger -> cofre "Lê as chaves de dados pessoais" "Arquivos da pasta de segredos"
        ledger -> consumidores "Entrega o evento EntryRegistered" "AMQP" "Assíncrono"
        ledger -> telemetria "Envia traces e métricas, e escreve logs no stdout" "OTLP" "Telemetria"

        canais -> api "Lança, estorna e consulta" "HTTPS, JSON, JWT"
        conciliacao -> api "Lê extrato e saldo em um instante, pede estorno" "HTTPS, JSON, JWT"
        plantao -> worker "Executa --migrate na implantação e --inspect-account na investigação" "Linha de comando"
        api -> db "Lê e escreve com o papel ledger_api. Um lançamento é uma transação só" "Npgsql, Dapper"
        api -> idp "Baixa as chaves públicas para validar tokens" "HTTPS, JWKS"
        api -> cofre "Lê as chaves de dados pessoais" "Arquivos da pasta de segredos"
        api -> telemetria "Envia traces e métricas" "OTLP" "Telemetria"
        worker -> db "Reivindica o outbox, poda, confere a integridade e recifra documentos, com o papel ledger_worker" "Npgsql, Dapper"
        worker -> broker "Publica EntryRegistered com confirmação" "AMQP" "Assíncrono"
        worker -> cofre "Lê as chaves de dados pessoais" "Arquivos da pasta de segredos"
        worker -> telemetria "Envia traces e métricas" "OTLP" "Telemetria"
        broker -> consumidores "Entrega às filas dos consumidores" "AMQP" "Assíncrono"

        canais -> forwardedHeaders "Lança, estorna e consulta" "HTTPS, JSON, JWT"
        conciliacao -> forwardedHeaders "Lê extrato e saldo em um instante, pede estorno" "HTTPS, JSON, JWT"
        forwardedHeaders -> correlationIdMiddleware "Repassa a requisição com o endereço e o esquema do cliente"
        correlationIdMiddleware -> securityHeadersMiddleware "Repassa com o X-Correlation-Id definido"
        securityHeadersMiddleware -> requestLogging "Repassa. Os cabeçalhos entram na resposta ao iniciar"
        requestLogging -> exceptionHandling "Repassa e registra a linha da requisição ao terminar"
        exceptionHandling -> requestTimeouts "Repassa e converte a falha em Problem Details"
        requestTimeouts -> authentication "Repassa com o prazo da requisição armado"
        authentication -> clientIdActivityMiddleware "Repassa o chamador autenticado"
        authentication -> idp "Baixa as chaves públicas, no modo Authority" "HTTPS, JWKS"
        clientIdActivityMiddleware -> rateLimiting "Repassa com o client_id no span"
        rateLimiting -> authorization "Repassa dentro dos limites"
        authorization -> accountsEndpoints "Encaminha a criação de conta"
        authorization -> entriesEndpoints "Encaminha lançamentos e estornos"
        authorization -> balanceEndpoints "Encaminha as consultas de saldo"
        authorization -> statementEndpoints "Encaminha o extrato"
        authorization -> healthEndpoints "Encaminha as rotas de saúde, que são anônimas"
        authorization -> openApiEndpoint "Encaminha o documento OpenAPI, quando habilitado"
        authorization -> deniedWriteAuditor "Entrega a escrita negada por escopo para a trilha"
        requestLogging -> apiObservability "Escreve a linha de log estruturada"
        healthEndpoints -> apiReadinessChecks "Executa as verificações marcadas como ready"
        apiReadinessChecks -> db "SELECT 1 e versão do esquema, com prazo de 1 segundo" "Npgsql"
        apiReadinessChecks -> keyProvider "Pede o autoteste do provedor de chaves"

        accountsEndpoints -> requestReaders "Lê e valida o corpo e a Idempotency-Key"
        entriesEndpoints -> requestReaders "Lê e valida o corpo e a Idempotency-Key"
        balanceEndpoints -> requestReaders "Lê o parâmetro asOf"
        statementEndpoints -> requestReaders "Lê from, to, limit e cursor"
        statementEndpoints -> cursorProtector "Decodifica o cursor recebido (IStatementCursorProtector)"
        accountsEndpoints -> createAccountHandler "Chama HandleAsync"
        entriesEndpoints -> registerEntryHandler "Chama HandleAsync"
        entriesEndpoints -> reverseEntryHandler "Chama HandleAsync"
        balanceEndpoints -> getBalanceHandler "Chama HandleAsync"
        statementEndpoints -> listEntriesHandler "Chama HandleAsync"

        registerEntryHandler -> domainEntries "Monta Money e Entry (Credit ou Debit)"
        reverseEntryHandler -> domainEntries "Planeja o estorno e monta Entry.ReversalOf"
        createAccountHandler -> domainAccounts "Normaliza e valida o documento do titular"
        getBalanceHandler -> domainAccounts "Monta o saldo e o limite da conta"
        listEntriesHandler -> domainEntries "Resolve StatementBounds"
        domainEntries -> domainShared "Usa Money, Result e Error"
        domainAccounts -> domainShared "Usa Money, Result e Error"

        registerEntryHandler -> entryWriteFlow "Entrega a função da transação"
        reverseEntryHandler -> entryWriteFlow "Entrega a função da transação"
        registerEntryHandler -> portasEscrita "Depende de IUnitOfWork e IIdGenerator"
        reverseEntryHandler -> portasEscrita "Depende de IUnitOfWork e IIdGenerator"
        entryWriteFlow -> portasEscrita "Usa as portas do escopo da transação"
        createAccountHandler -> portasEscrita "Depende de IUnitOfWork e IHolderDocumentProtector"
        getBalanceHandler -> portasLeitura "Depende de IBalanceReader"
        listEntriesHandler -> portasLeitura "Depende de IStatementReader e IStatementCursorProtector"
        getBalanceHandler -> readAudit "Audita a consulta"
        listEntriesHandler -> readAudit "Audita a consulta"
        readAudit -> apiObservability "Escreve na categoria Ledger.Audit"

        portasEscrita -> unitOfWork "IUnitOfWork é implementada por PostgresUnitOfWork"
        portasEscrita -> idempotencyStore "IIdempotencyStore é implementada por PostgresIdempotencyStore"
        portasEscrita -> entryRepository "IEntryRepository é implementada por PostgresEntryRepository"
        portasEscrita -> accountRepository "IAccountRepository é implementada por PostgresAccountRepository"
        portasEscrita -> outbox "IOutbox é implementada por PostgresOutbox"
        portasEscrita -> scopedAudit "IAuditTrail é implementada por PostgresScopedAuditTrail no escopo da transação"
        portasEscrita -> piiProtector "IHolderDocumentProtector é implementada por HolderDocumentProtector"
        portasLeitura -> balanceReader "IBalanceReader é implementada por PostgresBalanceReader"
        portasLeitura -> statementReader "IStatementReader é implementada por PostgresStatementReader"
        portasLeitura -> cursorProtector "IStatementCursorProtector é implementada por HmacStatementCursorProtector"

        unitOfWork -> db "BEGIN, COMMIT e ROLLBACK na fonte Write, com 7 conexões por instância" "Npgsql"
        idempotencyStore -> db "INSERT ON CONFLICT DO NOTHING e leitura da repetição em idempotency_keys" "Npgsql, Dapper"
        entryRepository -> db "UPDATE condicional em account_balances e INSERT em ledger_entries, e a leitura do original no estorno" "Npgsql, Dapper"
        accountRepository -> db "Cria conta e saldo, reserva a chave de criação e diagnostica a recusa" "Npgsql, Dapper"
        outbox -> db "INSERT em outbox_messages na mesma transação" "Npgsql, Dapper"
        scopedAudit -> db "INSERT em audit_log na mesma transação" "Npgsql, Dapper"
        deniedWriteAuditor -> db "INSERT em audit_log, fora da transação de negócio" "Npgsql, Dapper"
        balanceReader -> db "Lê account_balances e ledger_entries pela fonte Balance, somente leitura" "Npgsql, Dapper"
        statementReader -> db "Lê ledger_entries pela fonte Statement, somente leitura" "Npgsql, Dapper"
        createAccountHandler -> piiProtector "Cifra o documento e calcula o índice cego"
        piiProtector -> keyProvider "Pede as chaves por versão"
        keyProvider -> cofre "Lê a pasta de segredos" "Arquivos"
        apiObservability -> telemetria "Exporta traces e métricas" "OTLP" "Telemetria"

        plantao -> migrateCommand "Executa --migrate na implantação" "Linha de comando"
        plantao -> inspectAccountCommand "Executa --inspect-account=guid" "Linha de comando"
        migrateCommand -> migrationRunner "Chama o IDatabaseMigrator"
        migrationRunner -> db "Aplica os scripts SQL com o papel ledger_migrator" "Npgsql, DbUp"
        inspectAccountCommand -> inspectAccountIntegrityHandler "Chama HandleAsync"
        inspectAccountIntegrityHandler -> portasWorker "Depende de IIntegritySessions"

        workerHost -> outboxPublisherService "Base do laço"
        workerHost -> outboxPruneService "Base do laço"
        workerHost -> outboxMeasurementService "Base do laço"
        workerHost -> idempotencyPruneService "Base do laço"
        workerHost -> keyRewrapService "Base do laço"
        workerHost -> workerState "Registra o batimento de cada ciclo"

        outboxPublisherService -> publishOutboxBatchHandler "Chama HandleAsync a cada ciclo"
        outboxPruneService -> pruneOutboxHandler "Chama HandleAsync a cada ciclo"
        outboxMeasurementService -> measureOutboxHandler "Chama HandleAsync a cada ciclo"
        idempotencyPruneService -> pruneIdempotencyKeysHandler "Chama HandleAsync a cada ciclo"
        integrityCheckService -> runIntegrityCheckHandler "Chama HandleAsync nos dois laços"
        keyRewrapService -> rewrapAccountsHandler "Chama HandleAsync a cada ciclo"
        keyRewrapService -> recordKeyActivationHandler "Chama HandleAsync uma vez, na subida"

        publishOutboxBatchHandler -> portasWorker "Depende de IOutboxQueue e IEventPublisher"
        pruneOutboxHandler -> portasWorker "Depende de IOutboxQueue"
        measureOutboxHandler -> portasWorker "Depende de IOutboxQueue"
        pruneIdempotencyKeysHandler -> portasWorker "Depende de IIdempotencyKeyPruner"
        runIntegrityCheckHandler -> portasWorker "Depende de IIntegritySessions"
        rewrapAccountsHandler -> portasWorker "Depende de IAccountKeyRewrapper e IHolderDocumentProtector"
        recordKeyActivationHandler -> portasWorker "Depende de IKeyProvider e IAuditTrail"

        portasWorker -> outboxQueue "IOutboxQueue é implementada por PostgresOutboxQueue"
        portasWorker -> circuitBreakingPublisher "IEventPublisher é implementada por CircuitBreakingEventPublisher"
        portasWorker -> idempotencyKeyPruner "IIdempotencyKeyPruner é implementada por PostgresIdempotencyKeyPruner"
        portasWorker -> integritySessions "IIntegritySessions é implementada por PostgresIntegritySessions"
        portasWorker -> keyRewrapper "IAccountKeyRewrapper é implementada por PostgresAccountKeyRewrapper"
        portasWorker -> migrationRunner "IDatabaseMigrator é implementada por MigrationRunner"
        portasWorker -> wrkAuditTrail "IAuditTrail é implementada por PostgresAuditTrail"
        portasWorker -> wrkPiiProtector "IHolderDocumentProtector é implementada por HolderDocumentProtector"

        circuitBreakingPublisher -> rabbitMqPublisher "Delega a publicação protegida pelo circuito"
        outboxQueue -> db "Reivindica com FOR UPDATE SKIP LOCKED, marca published_at, devolve e poda" "Npgsql, Dapper"
        idempotencyKeyPruner -> db "DELETE em lotes em idempotency_keys" "Npgsql, Dapper"
        integritySessions -> db "Lê ledger_entries e account_balances e grava a execução em audit_log" "Npgsql, Dapper"
        keyRewrapper -> db "Lê e atualiza as colunas do documento em accounts" "Npgsql, Dapper"
        wrkAuditTrail -> db "INSERT em audit_log" "Npgsql, Dapper"
        rabbitMqPublisher -> broker "Publica na exchange ledger.events com confirmação" "AMQP, AMQPS em produção" "Assíncrono"
        rewrapAccountsHandler -> wrkPiiProtector "Reprotege o documento"
        wrkPiiProtector -> wrkKeyProvider "Pede as chaves por versão"
        wrkKeyProvider -> cofre "Lê a pasta de segredos" "Arquivos"

        outboxMeasurementService -> workerState "Escreve as medidas do outbox"
        runIntegrityCheckHandler -> workerState "Registra o batimento durante a execução"
        rewrapAccountsHandler -> workerState "Escreve o uso das versões de chave"
        workerHealth -> workerReadinessChecks "Executa as verificações live e ready"
        workerReadinessChecks -> workerState "Lê batimento, medidas e uso de chaves"
        workerReadinessChecks -> db "SELECT 1 e versão do esquema, com prazo de 1 segundo" "Npgsql"
        workerReadinessChecks -> circuitBreakingPublisher "Lê o estado do circuito"
        workerReadinessChecks -> rabbitMqPublisher "Sonda a conexão com o broker, que vira Degraded e nunca 503" "AMQP"
        publishOutboxBatchHandler -> wrkObservability "Registra publicação, falha e estado do circuito"
        runIntegrityCheckHandler -> wrkObservability "Emite IntegrityViolationDetected e a métrica do alerta"
        wrkObservability -> telemetria "Exporta traces e métricas" "OTLP" "Telemetria"

        deploymentEnvironment "Local" {

            deploymentNode "Máquina de desenvolvimento ou executor de CI" "Windows, macOS ou Linux com Docker." "Docker Desktop ou Docker Engine" {

                deploymentNode "Docker Compose" "Projeto ledger, ou o nome de COMPOSE_PROJECT_NAME. Rede interna, sem TLS, um nó de cada coisa e portas publicadas só em 127.0.0.1." "Docker Compose v2" {
                    migrator = infrastructureNode "migrator" "Serviço de execução única: aplica os scripts SQL com o papel ledger_migrator e termina antes de api e worker subirem." "Imagem ledger-worker com --migrate"
                    devKeys = infrastructureNode "dev-keys" "Serviço de execução única: grava a chave pública de desenvolvimento num volume para a API validar tokens." "alpine:3.20"
                    otel = infrastructureNode "otel" "Só com o perfil observability: coletor OTLP e Grafana, para olhar traces e métricas." "grafana/otel-lgtm"
                    k6 = infrastructureNode "k6" "Só com o arquivo de teste e o perfil load: gera a carga contra a API na rede do Compose." "grafana/k6"

                    deploymentNode "postgres" "Instância única, sem standby. O script de inicialização cria o banco e os quatro papéis." "PostgreSQL 16" {
                        localDb = containerInstance db
                    }

                    deploymentNode "rabbitmq" "Nó único, com o plugin de gerenciamento." "RabbitMQ 3.13" {
                        localBroker = containerInstance broker
                    }

                    deploymentNode "api" "Autenticação em modo LocalKey, com tokens emitidos por um script de apoio. Porta 8080." "Imagem ledger-api" {
                        localApi = containerInstance api
                    }

                    deploymentNode "worker" "Chaves de dados pessoais pelo ConfigurationKeyProvider. Saúde na porta 8081." "Imagem ledger-worker" {
                        localWorker = containerInstance worker
                    }
                }
            }

            migrator -> localDb "Aplica os scripts SQL (DbUp)" "Npgsql"
            devKeys -> localApi "Entrega a chave pública pelo volume ledger-dev-keys" "Volume"
            localApi -> otel "Envia traces e métricas" "OTLP" "Telemetria"
            localWorker -> otel "Envia traces e métricas" "OTLP" "Telemetria"
            k6 -> localApi "Gera carga" "HTTP"
        }

        deploymentEnvironment "Produção" {

            deploymentGroup "servico"

            deploymentNode "Pipeline de implantação" "Roda antes de cada versão. A API e o Worker em operação não têm privilégio de DDL." "Azure DevOps" {
                migracao = infrastructureNode "Etapa de migração" "Ledger.Worker com --migrate e o papel ledger_migrator. Execuções simultâneas se serializam pela trava consultiva." "Imagem do Ledger.Worker"
            }

            deploymentNode "Rede interna do banco" "Região única com três zonas de disponibilidade. TLS 1.2 ou superior em todos os saltos. Topologia proposta, ainda não exercitada." "Orquestrador de contêineres e máquinas virtuais" {

                balanceador = infrastructureNode "Balanceador de carga" "Termina TLS, tira do rodízio a instância que falha na readiness e, nas rotas de lançamento, balanceia por hash do accountId." "Balanceador L7"
                segredos = infrastructureNode "Pasta de segredos" "Entrega as chaves de dados pessoais, por versão, como arquivos montados nos contêineres. A origem do conteúdo (cofre externo) é decisão de implantação." "Volume montado"

                deploymentNode "Zona A" "Zona do primário do PostgreSQL." "Zona de disponibilidade" "Zona" {

                    deploymentNode "Ledger.Api" "Duas réplicas por zona: quatro no pico e seis como teto, o que o orçamento de conexões comporta." "Contêiner .NET 10" "" 2 {
                        apiZonaA = containerInstance api "servico"
                    }

                    deploymentNode "Ledger.Worker" "Uma instância por zona. As duas dividem o outbox sem se bloquear." "Contêiner .NET 10" {
                        workerZonaA = containerInstance worker "servico"
                    }

                    deploymentNode "PostgreSQL primário" "Recebe toda a escrita. Premissa de dimensionamento: 16 vCPU, max_connections em 200 e WAL em disco separado." "PostgreSQL 16" {
                        pgPrimario = containerInstance db "servico"
                    }

                    deploymentNode "RabbitMQ nó 1" "Nó do cluster. As filas quorum exigem maioria, dois de três." "RabbitMQ 3.13" {
                        brokerZonaA = containerInstance broker "servico"
                    }
                }

                deploymentNode "Zona B" "Zona do standby síncrono." "Zona de disponibilidade" "Zona" {

                    deploymentNode "Ledger.Api" "Duas réplicas por zona: quatro no pico e seis como teto, o que o orçamento de conexões comporta." "Contêiner .NET 10" "" 2 {
                        apiZonaB = containerInstance api "servico"
                    }

                    deploymentNode "Ledger.Worker" "Uma instância por zona. As duas dividem o outbox sem se bloquear." "Contêiner .NET 10" {
                        workerZonaB = containerInstance worker "servico"
                    }

                    deploymentNode "PostgreSQL standby síncrono" "Alta disponibilidade, não é réplica de leitura: nenhuma consulta de negócio vai para ele." "PostgreSQL 16" {
                        pgStandbyA = infrastructureNode "Réplica em streaming (síncrona)" "Recebe o WAL do primário e confirma o commit. Nenhum contêiner do ledger fala com ela." "PostgreSQL 16"
                    }

                    deploymentNode "RabbitMQ nó 2" "Nó do cluster. As filas quorum exigem maioria, dois de três." "RabbitMQ 3.13" {
                        brokerZonaB = containerInstance broker "servico"
                    }
                }

                deploymentNode "Zona C" "Zona do segundo standby, do terceiro nó do broker e do orquestrador de failover." "Zona de disponibilidade" "Zona" {

                    deploymentNode "PostgreSQL segundo standby" "Recomendado: com dois standbys o commit síncrono usa quórum ANY 1 e a queda de um deles não trava a escrita." "PostgreSQL 16" {
                        pgStandbyB = infrastructureNode "Réplica em streaming (quórum)" "Recebe o WAL do primário e participa do quórum do commit síncrono. Nenhum contêiner do ledger fala com ela." "PostgreSQL 16"
                    }

                    deploymentNode "RabbitMQ nó 3" "Nó do cluster. As filas quorum exigem maioria, dois de três." "RabbitMQ 3.13" {
                        brokerZonaC = containerInstance broker "servico"
                    }

                    orquestrador = infrastructureNode "Orquestrador de failover" "Vigia o primário e promove um standby. Nunca rebaixa o commit para assíncrono sozinho." "Orquestrador de alta disponibilidade do PostgreSQL"
                }
            }

            balanceador -> apiZonaA "Encaminha o tráfego das instâncias prontas" "HTTPS, TLS 1.2 ou superior"
            balanceador -> apiZonaB "Encaminha o tráfego das instâncias prontas" "HTTPS, TLS 1.2 ou superior"
            migracao -> pgPrimario "Aplica os scripts SQL (DbUp)" "Npgsql, TLS"
            segredos -> apiZonaA "Fornece as chaves de dados pessoais" "Arquivos"
            segredos -> apiZonaB "Fornece as chaves de dados pessoais" "Arquivos"
            segredos -> workerZonaA "Fornece as chaves de dados pessoais" "Arquivos"
            segredos -> workerZonaB "Fornece as chaves de dados pessoais" "Arquivos"
            pgPrimario -> pgStandbyA "Replicação em streaming com commit síncrono" "Replicação nativa do PostgreSQL, TLS" "Replicação"
            pgPrimario -> pgStandbyB "Replicação em streaming com commit síncrono" "Replicação nativa do PostgreSQL, TLS" "Replicação"
            orquestrador -> pgPrimario "Vigia a saúde e promove um standby no failover" "Protocolo do orquestrador"
            orquestrador -> pgStandbyA "Vigia a saúde e promove um standby no failover" "Protocolo do orquestrador"
            orquestrador -> pgStandbyB "Vigia a saúde e promove um standby no failover" "Protocolo do orquestrador"
        }
    }

    views {

        systemContext ledger "Contexto" "Onde o ledger fica no mapa do banco e com quem ele conversa." {
            title "Ledger: contexto do sistema"
            include *
            autoLayout lr
        }

        container ledger "Containers" "Dois executáveis do mesmo código, o banco e o broker. A API não fala com o broker." {
            title "Ledger: containers"
            include *
            autoLayout lr
        }

        component api "ApiCadeiaHttp" "A ordem da cadeia de requisição, do cliente ao endpoint." {
            title "Ledger.Api: cadeia de requisição"
            include canais conciliacao forwardedHeaders correlationIdMiddleware securityHeadersMiddleware requestLogging exceptionHandling requestTimeouts authentication clientIdActivityMiddleware rateLimiting authorization idp deniedWriteAuditor apiObservability accountsEndpoints entriesEndpoints balanceEndpoints statementEndpoints healthEndpoints openApiEndpoint
            autoLayout lr
        }

        component api "ApiEscrita" "Contas, lançamentos e estornos: rotas, leitores, casos de uso, portas e adaptadores." {
            title "Ledger.Api: caminho de escrita"
            include accountsEndpoints entriesEndpoints requestReaders createAccountHandler registerEntryHandler reverseEntryHandler entryWriteFlow domainEntries domainAccounts domainShared portasEscrita unitOfWork idempotencyStore entryRepository accountRepository outbox scopedAudit piiProtector keyProvider db cofre
            autoLayout lr
        }

        component api "ApiLeitura" "Saldo, extrato, saúde e documento OpenAPI: rotas, casos de uso, portas e adaptadores." {
            title "Ledger.Api: caminho de leitura"
            include balanceEndpoints statementEndpoints healthEndpoints openApiEndpoint requestReaders getBalanceHandler listEntriesHandler readAudit portasLeitura balanceReader statementReader cursorProtector apiReadinessChecks keyProvider apiObservability db
            autoLayout lr
        }

        component worker "WorkerPublicacao" "Publicação do outbox, poda e medição do acúmulo." {
            title "Ledger.Worker: publicação do outbox"
            include workerHost outboxPublisherService outboxPruneService outboxMeasurementService publishOutboxBatchHandler pruneOutboxHandler measureOutboxHandler portasWorker outboxQueue circuitBreakingPublisher rabbitMqPublisher workerState wrkObservability db broker
            autoLayout lr
        }

        component worker "WorkerIntegridadeEChaves" "Conferência de integridade, poda de chaves de idempotência e recifragem de documentos." {
            title "Ledger.Worker: integridade, chaves e poda de idempotência"
            include workerHost integrityCheckService idempotencyPruneService keyRewrapService runIntegrityCheckHandler pruneIdempotencyKeysHandler rewrapAccountsHandler recordKeyActivationHandler portasWorker integritySessions idempotencyKeyPruner keyRewrapper wrkAuditTrail wrkPiiProtector wrkKeyProvider db cofre
            autoLayout lr
        }

        component worker "WorkerComandosESaude" "Modos de linha de comando e verificações de saúde." {
            title "Ledger.Worker: comandos e saúde"
            include plantao migrateCommand inspectAccountCommand inspectAccountIntegrityHandler migrationRunner portasWorker workerHealth workerReadinessChecks workerState circuitBreakingPublisher rabbitMqPublisher integritySessions db broker
            autoLayout lr
        }

        dynamic api "DinamicaRegistrarLancamento" "Do POST de um lançamento até o commit. A repetição com a mesma chave devolve o lançamento original." {
            title "Registrar um lançamento"
            canais -> forwardedHeaders "POST /v1/accounts/{accountId}/entries com JWT, Idempotency-Key e o corpo do lançamento"
            forwardedHeaders -> correlationIdMiddleware "Repassa a requisição"
            correlationIdMiddleware -> securityHeadersMiddleware "Define o X-Correlation-Id e repassa"
            securityHeadersMiddleware -> requestLogging "Repassa"
            requestLogging -> exceptionHandling "Repassa"
            exceptionHandling -> requestTimeouts "Repassa"
            requestTimeouts -> authentication "Arma o prazo e repassa"
            authentication -> clientIdActivityMiddleware "Valida o JWT, ou responde 401"
            clientIdActivityMiddleware -> rateLimiting "Repassa"
            rateLimiting -> authorization "Aplica concorrência e cotas, ou responde 503 ou 429"
            authorization -> entriesEndpoints "Exige ledger.write, ou responde 403"
            entriesEndpoints -> requestReaders "Lê o corpo e a chave de forma estrita"
            entriesEndpoints -> registerEntryHandler "Chama HandleAsync"
            registerEntryHandler -> entryWriteFlow "Entrega a função da transação ao IUnitOfWork"
            entryWriteFlow -> portasEscrita "Passo 1: reserva a chave. Se ela já existe, compara o hash e devolve a repetição ou 422"
            portasEscrita -> idempotencyStore "IIdempotencyStore: INSERT ON CONFLICT DO NOTHING em idempotency_keys"
            entryWriteFlow -> portasEscrita "Passo 2: aplica o saldo e grava o lançamento"
            portasEscrita -> entryRepository "IEntryRepository: UPDATE condicional e INSERT numa instrução só"
            entryWriteFlow -> portasEscrita "Passo 3: grava o EntryRegistered"
            portasEscrita -> outbox "IOutbox: INSERT em outbox_messages"
            portasEscrita -> unitOfWork "Passo 4: o IUnitOfWork confirma a transação"
            unitOfWork -> db "COMMIT. Só depois a API responde 201 com o saldo resultante"
            autoLayout lr
        }

        dynamic api "DinamicaSaldoEmUmInstante" "Consulta com asOf: uma busca por índice, sem somar histórico." {
            title "Consulta de saldo em um instante"
            conciliacao -> forwardedHeaders "GET /v1/accounts/{accountId}/balance?asOf= com um instante UTC e o JWT"
            forwardedHeaders -> correlationIdMiddleware "Repassa a requisição"
            correlationIdMiddleware -> securityHeadersMiddleware "Define o X-Correlation-Id e repassa"
            securityHeadersMiddleware -> requestLogging "Repassa"
            requestLogging -> exceptionHandling "Repassa"
            exceptionHandling -> requestTimeouts "Repassa"
            requestTimeouts -> authentication "Arma o prazo e repassa"
            authentication -> clientIdActivityMiddleware "Valida o JWT, ou responde 401"
            clientIdActivityMiddleware -> rateLimiting "Repassa"
            rateLimiting -> authorization "Aplica a concorrência de saldo e a cota de leitura"
            authorization -> balanceEndpoints "Exige ledger.read, ou responde 403"
            balanceEndpoints -> requestReaders "Valida o asOf, que precisa ser UTC"
            balanceEndpoints -> getBalanceHandler "Chama HandleAsync"
            getBalanceHandler -> portasLeitura "Pede o último lançamento da conta com recorded_at até T (IBalanceReader)"
            portasLeitura -> balanceReader "IBalanceReader: leitura na fonte Balance"
            balanceReader -> db "SELECT pelo índice de conta e recorded_at, em sessão somente leitura"
            autoLayout lr
        }

        dynamic worker "DinamicaOutboxBrokerIndisponivel" "A escrita segue normal enquanto o broker está fora. O outbox acumula, o circuito abre e tudo é publicado quando o broker volta." {
            title "Publicação do outbox com o broker indisponível"
            outboxPublisherService -> publishOutboxBatchHandler "Chama HandleAsync"
            publishOutboxBatchHandler -> portasWorker "Reivindica o lote (IOutboxQueue): FOR UPDATE SKIP LOCKED, locked_until à frente e attempts mais um"
            portasWorker -> outboxQueue "IOutboxQueue: reivindicação numa transação curta"
            outboxQueue -> db "UPDATE com RETURNING"
            publishOutboxBatchHandler -> portasWorker "Publica o lote fora de transação (IEventPublisher)"
            portasWorker -> circuitBreakingPublisher "IEventPublisher: publicação protegida pelo circuito"
            circuitBreakingPublisher -> rabbitMqPublisher "Delega a publicação"
            rabbitMqPublisher -> broker "A publicação falha por falta de confirmação. As mensagens ficam sem published_at"
            publishOutboxBatchHandler -> wrkObservability "Registra a falha e a mudança de estado do circuito"
            wrkObservability -> telemetria "Exporta a idade da mensagem pendente mais antiga e o estado do circuito"
            autoLayout lr
        }

        deployment ledger "Local" "ImplantacaoLocal" "Docker Compose numa máquina só: sem standby, sem emissor de tokens e sem cofre de chaves." {
            title "Ledger: implantação local"
            include *
            autoLayout lr
        }

        deployment ledger "Produção" "ImplantacaoProducao" "Três zonas: réplicas da API e do Worker, PostgreSQL com primário e standbys síncronos e broker em cluster. Topologia proposta." {
            title "Ledger: implantação em produção (proposta)"
            include *
            autoLayout lr
        }

        styles {
            element "Element" {
                fontSize 22
            }
            element "Pessoa" {
                background #08427b
                color #ffffff
                shape Person
            }
            element "Sistema em foco" {
                background #1168bd
                color #ffffff
            }
            element "Sistema externo" {
                background #6b6b6b
                color #ffffff
            }
            element "Container" {
                background #2c6fbb
                color #ffffff
            }
            element "Banco de dados" {
                shape Cylinder
            }
            element "Broker de mensagens" {
                shape Pipe
            }
            element "Cadeia HTTP" {
                background #1d4ed8
                color #ffffff
            }
            element "Rota" {
                background #2563eb
                color #ffffff
            }
            element "Caso de uso" {
                background #0e7490
                color #ffffff
            }
            element "Porta" {
                background #0f766e
                color #ffffff
            }
            element "Domínio" {
                background #15803d
                color #ffffff
            }
            element "Infraestrutura" {
                background #6d28d9
                color #ffffff
            }
            element "Serviço de fundo" {
                background #be123c
                color #ffffff
            }
            element "Deployment Node" {
                color #1f2937
            }
            element "Zona" {
                background #f8fafc
                color #0f172a
                border dashed
            }
            element "Infrastructure Node" {
                background #475569
                color #ffffff
            }
            relationship "Relationship" {
                thickness 2
                color #475569
                fontSize 20
            }
            relationship "Assíncrono" {
                dashed true
                color #b45309
            }
            relationship "Telemetria" {
                dashed true
                color #6b7280
            }
            relationship "Replicação" {
                thickness 4
                color #b91c1c
            }
        }
    }
}
