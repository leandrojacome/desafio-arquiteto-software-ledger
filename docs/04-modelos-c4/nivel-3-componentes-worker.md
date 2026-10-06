# C4 nível 3: componentes do Worker

O `Ledger.Worker` é uma aplicação ASP.NET Core com serviços de fundo. Ninguém o chama: ele acorda sozinho, olha o banco e age, e o único tráfego de entrada é o de saúde, na porta 8081. O mesmo executável tem três modos. Sem argumento, sobe os seis serviços de fundo e a saúde. Com `--migrate`, registra só a infraestrutura com o papel `ledger_migrator`, aplica as migrações e termina. Com `--inspect-account=<guid>`, registra a aplicação sem os laços, confere uma conta e termina, e nos dois modos de comando o código de saída diz o resultado.

O Worker se apoia nas portas da camada de aplicação e só chega ao banco com o papel `ledger_worker`, que lê o ledger e não insere nada nele. Várias instâncias podem rodar lado a lado: o lote do outbox é reivindicado com `FOR UPDATE SKIP LOCKED`, e a conferência de integridade e a recifragem usam travas consultivas para só uma instância trabalhar por vez. Cadência, lote e prazo de cada serviço estão em [Configuração e linha de comando](../05-contratos/configuracao.md).

## Publicação do outbox, poda e medição

O `OutboxPublisherService` reivindica lotes de `outbox_messages`, publica no RabbitMQ esperando a confirmação, por meio do `CircuitBreakingEventPublisher`, que envolve o `RabbitMqEventPublisher`, e só depois marca as mensagens como publicadas. Com o circuito aberto o handler nem reivindica: as mensagens esperam no banco e uma sonda sem mensagem testa o broker ([publicação do outbox](../06-fluxos/publicacao-do-outbox.md)). O `OutboxPruneService` apaga o que foi publicado além da retenção, e o `OutboxMeasurementService` mede o acúmulo e a idade da pendente mais antiga, que alimentam a readiness e as métricas.

```mermaid
---
config:
  flowchart:
    wrappingWidth: 260
    nodeSpacing: 30
    rankSpacing: 50
---
flowchart TB
    subgraph host ["Ledger.Worker: serviços de fundo"]
        direction TB
        base["<b>WorkerLoopHost e WorkerLoopService</b><br/><i>[BackgroundService]</i><br/>Escopo por ciclo, recuo exponencial na falha e batimento ao fim de cada ciclo"]
        pub["<b>OutboxPublisherService</b><br/><i>[BackgroundService]</i><br/>Reivindica, publica com confirmação e marca. Sem pausa quando o lote vem cheio"]
        prune["<b>OutboxPruneService</b><br/><i>[BackgroundService]</i><br/>A cada Outbox:PruneIntervalMinutes poda o que passou de Outbox:RetentionDays"]
        meas["<b>OutboxMeasurementService</b><br/><i>[BackgroundService]</i><br/>A cada Outbox:MeasureIntervalSeconds mede o acúmulo e a idade da mais antiga"]
        base -->|"Base do laço"| pub
        base -->|"Base do laço"| prune
        base -->|"Base do laço"| meas
    end

    subgraph app ["Ledger.Application: casos de uso e portas"]
        direction TB
        hpub["<b>PublishOutboxBatchHandler</b><br/><i>[Handler]</i><br/>Conecta, sonda o circuito, reivindica, publica em paralelo e marca o que o broker confirmou"]
        hprune["<b>PruneOutboxHandler</b><br/><i>[Handler]</i><br/>Repete a poda em lotes até esvaziar"]
        hmeas["<b>MeasureOutboxHandler</b><br/><i>[Handler]</i><br/>Lê o acúmulo e a idade da mais antiga"]
        portas["<b>Portas</b><br/><i>[Interfaces]</i><br/>IOutboxQueue, IEventPublisher, IOutboxTelemetry e IWorkerHeartbeat"]
        hpub --> portas
        hprune --> portas
        hmeas --> portas
    end

    subgraph infra ["Ledger.Infrastructure: adaptadores"]
        direction TB
        queue["<b>PostgresOutboxQueue</b><br/><i>[Npgsql, Dapper]</i><br/>Reivindica com FOR UPDATE SKIP LOCKED, marca, devolve, poda e mede"]
        cb["<b>CircuitBreakingEventPublisher</b><br/><i>[Polly]</i><br/>Abre o circuito pela proporção de falhas, contadas por mensagem"]
        rmq["<b>RabbitMqEventPublisher e BrokerConnection</b><br/><i>[RabbitMQ.Client]</i><br/>Publica com confirmação e mandatory, declara a topologia e reconecta com recuo"]
        estado["<b>Estado de saúde</b><br/><i>[Singletons]</i><br/>WorkerHeartbeat e OutboxStatsHolder"]
        cb --> rmq
    end

    db[("<b>PostgreSQL 16</b><br/><i>[Container]</i>")]
    broker(["<b>RabbitMQ 3.13</b><br/><i>[Container]</i>"])

    pub -->|"HandleAsync"| hpub
    prune -->|"HandleAsync"| hprune
    meas -->|"HandleAsync"| hmeas
    portas -.->|"IOutboxQueue"| queue
    portas -.->|"IEventPublisher"| cb
    portas -.->|"Batimento e medidas"| estado
    queue -->|"UPDATE e DELETE em outbox_messages<br/><i>Npgsql, papel ledger_worker</i>"| db
    rmq -->|"EntryRegistered na exchange ledger.events<br/><i>AMQP</i>"| broker

    classDef component fill:#85bbf0,stroke:#3b6ea5,color:#000000
    classDef container fill:#2c6fbb,stroke:#1d4f87,color:#ffffff
    class base,pub,prune,meas,hpub,hprune,hmeas,portas,queue,cb,rmq,estado component
    class db,broker container
    style host fill:none,stroke:#1168bd,stroke-width:2px,stroke-dasharray:6 4
    style app fill:none,stroke:#1168bd,stroke-width:2px,stroke-dasharray:6 4
    style infra fill:none,stroke:#1168bd,stroke-width:2px,stroke-dasharray:6 4
```

| Componente | Responsabilidade |
|---|---|
| `WorkerLoopHost`, `WorkerLoopService` (`Ledger.Worker`) | Base de cinco dos seis serviços (o `IntegrityCheckService` tem laços próprios): escopo de injeção por ciclo, recuo exponencial depois de falha e batimento ao fim de cada ciclo |
| `OutboxPublisherService` | Volta sem pausa com o lote cheio, espera pouco com o lote incompleto e um pouco mais quando não reivindicou nada (broker desconectado ou circuito não fechado). No desligamento, o `DrainToken` dá ao handler `Resilience:ShutdownTimeoutSeconds` menos 5 s para terminar de marcar |
| `OutboxPruneService`, `OutboxMeasurementService` | A poda remove, em lotes, as publicadas além de `Outbox:RetentionDays`: remoção física de uma fila de integração, nunca do ledger. A medição conta as pendentes, a idade da mais antiga e as que passaram do limite de tentativas |
| `PublishOutboxBatchHandler` (`Ledger.Application.Outbox`) | Conecta ao broker se preciso, sonda o circuito quando não está fechado, reivindica até o orçamento do publicador, publica em paralelo dentro do prazo de confirmação e marca as confirmadas. As que nem foram tentadas voltam à fila e devolvem a tentativa gasta, e marcar e devolver têm prazo próprio ([documento de arquitetura 0030](../03-principios-e-decisoes/documento-arquitetura/0030-prazo-proprio-para-marcar-as-mensagens-publicadas.md)) |
| `PruneOutboxHandler`, `MeasureOutboxHandler` | Repetem a poda até um lote sair incompleto e leem as estatísticas do outbox, publicando-as como métricas |
| `PostgresOutboxQueue` (`Ledger.Infrastructure.Persistence.Outbox`) | Dono do SQL da fila (reivindicação com aluguel, marcação, devolução, poda e estatística), em [publicação do outbox](../06-fluxos/publicacao-do-outbox.md) e [rotinas de manutenção](../06-fluxos/rotinas-de-manutencao.md) |
| `CircuitBreakingEventPublisher` (`Ledger.Infrastructure.Messaging`) | Circuit breaker do Polly: abre quando a proporção de falhas na janela passa do limite, com um mínimo de mensagens, e fica aberto por um tempo fixo (`Resilience:BrokerCircuitBreaker`). Falha de serialização e mensagem sem rota não contam |
| `RabbitMqEventPublisher`, `BrokerConnection` | Publicam com confirmação e `mandatory`: a mensagem que nenhuma fila recebe volta do broker e permanece no outbox. Declaram a exchange e a fila de retenção a cada conexão e reconectam com recuo exponencial. Levam o `traceparent` guardado na linha, e o span `outbox.publish` nasce como raiz de um trace novo, com link para o da requisição original |
| `WorkerHeartbeat`, `OutboxStatsHolder` | Guardam o último batimento de cada laço e a última medida do outbox, que as verificações de saúde leem |

## Integridade, chaves e poda de idempotência

O `IntegrityCheckService` confere periodicamente se o ledger ainda diz a verdade e nunca corrige nada: lê, compara, registra e alerta. A execução recente roda a cada poucos minutos sobre a janela desde a última, e a completa uma vez por dia sobre todas as contas, cada modo com a sua trava consultiva. O `IdempotencyPruneService` apaga as chaves vencidas, e o `KeyRewrapService` registra a versão ativa da chave na subida e recifra, em lotes, os documentos gravados sob uma versão antiga.

```mermaid
---
config:
  flowchart:
    wrappingWidth: 260
    nodeSpacing: 30
    rankSpacing: 50
---
flowchart TB
    subgraph host ["Ledger.Worker: serviços de fundo"]
        direction TB
        integ["<b>IntegrityCheckService</b><br/><i>[BackgroundService]</i><br/>Dois laços: execução recente e execução completa. Só detecta e registra"]
        idem["<b>IdempotencyPruneService</b><br/><i>[BackgroundService]</i><br/>Poda as chaves de idempotência vencidas, em lotes"]
        rew["<b>KeyRewrapService</b><br/><i>[BackgroundService]</i><br/>Registra a versão ativa na subida e recifra os documentos de versão antiga"]
    end

    subgraph app ["Ledger.Application: casos de uso e portas"]
        direction TB
        hint["<b>RunIntegrityCheckHandler</b><br/><i>[Handler]</i><br/>Percorre contas e cadeia em fatias, classifica cada achado e registra a execução"]
        hidem["<b>PruneIdempotencyKeysHandler</b><br/><i>[Handler]</i><br/>Repete a poda em lotes até esvaziar"]
        hrew["<b>RewrapAccountsHandler</b><br/><i>[Handler]</i><br/>Decifra com a versão gravada e cifra com a ativa, em lotes"]
        hact["<b>RecordKeyActivationHandler</b><br/><i>[Handler]</i><br/>Grava keys.version_activated uma vez por versão ativa"]
        portas["<b>Portas</b><br/><i>[Interfaces]</i><br/>IIntegritySessions, IIdempotencyKeyPruner, IAccountKeyRewrapper, IHolderDocumentProtector, IKeyProvider e IAuditTrail"]
        hint --> portas
        hidem --> portas
        hrew --> portas
        hact --> portas
    end

    subgraph infra ["Ledger.Infrastructure: adaptadores"]
        direction TB
        sess["<b>PostgresIntegritySessions</b><br/><i>[Npgsql, Dapper]</i><br/>Abre a sessão sob trava por modo, roda as consultas de topo e de cadeia e grava a execução e os achados"]
        pruner["<b>PostgresIdempotencyKeyPruner</b><br/><i>[Npgsql, Dapper]</i><br/>Remove as chaves vencidas de idempotency_keys e account_creation_keys"]
        rewr["<b>PostgresAccountKeyRewrapper</b><br/><i>[Npgsql, Dapper]</i><br/>Seleciona lotes sob trava consultiva e atualiza as colunas do documento"]
        trail["<b>PostgresAuditTrail</b><br/><i>[Npgsql, Dapper]</i><br/>INSERT em audit_log"]
        prot["<b>HolderDocumentProtector</b><br/><i>[AES-GCM, HMAC]</i><br/>Decifra com a versão antiga e cifra com a ativa"]
        keyp["<b>ReloadingKeyProvider</b><br/><i>[IKeyProvider]</i><br/>Versões vivas lidas da pasta de segredos"]
        sess ~~~ rewr
        pruner ~~~ trail
        prot --> keyp
    end

    db[("<b>PostgreSQL 16</b><br/><i>[Container]</i>")]
    cofre["<b>Cofre de chaves</b><br/><i>[Sistema externo]</i><br/>Pasta de segredos"]

    integ -->|"HandleAsync"| hint
    idem -->|"HandleAsync"| hidem
    rew -->|"HandleAsync"| hrew
    rew -->|"HandleAsync, uma vez"| hact
    portas -.->|"IIntegritySessions"| sess
    portas -.->|"IIdempotencyKeyPruner"| pruner
    portas -.->|"IAccountKeyRewrapper"| rewr
    portas -.->|"IAuditTrail"| trail
    portas -.->|"IHolderDocumentProtector"| prot
    sess -->|"SELECT em ledger_entries e account_balances, INSERT em audit_log"| db
    pruner -->|"DELETE em lotes"| db
    rewr -->|"SELECT e UPDATE em accounts"| db
    trail -->|"INSERT em audit_log"| db
    keyp -.->|"Lê os arquivos"| cofre

    classDef component fill:#85bbf0,stroke:#3b6ea5,color:#000000
    classDef external fill:#6b6b6b,stroke:#4a4a4a,color:#ffffff
    classDef container fill:#2c6fbb,stroke:#1d4f87,color:#ffffff
    class integ,idem,rew,hint,hidem,hrew,hact,portas,sess,pruner,rewr,trail,prot,keyp component
    class cofre external
    class db container
    style host fill:none,stroke:#1168bd,stroke-width:2px,stroke-dasharray:6 4
    style app fill:none,stroke:#1168bd,stroke-width:2px,stroke-dasharray:6 4
    style infra fill:none,stroke:#1168bd,stroke-width:2px,stroke-dasharray:6 4
```

| Componente | Responsabilidade |
|---|---|
| `IntegrityCheckService` | Laço recente, com sobreposição sobre a janela anterior, e laço completo, que só começa depois da primeira execução recente e roda quando passou o intervalo da última, lida em `audit_log`. Registra o batimento de cada laço |
| `IdempotencyPruneService`, `KeyRewrapService` | O primeiro remove, em lotes, as chaves mais velhas que 35 dias ([documento de arquitetura 0017](../03-principios-e-decisoes/documento-arquitetura/0017-retencao-de-35-dias-das-chaves-de-idempotencia.md)). O segundo chama o `RecordKeyActivationHandler` na subida e depois recifra, em lotes e em intervalo fixo, os documentos de versão antiga |
| `RunIntegrityCheckHandler` (`Ledger.Application.Integrity`) | Na execução completa confere o topo de todas as contas e a cadeia de `balance_after` em fatias de tempo, e na recente só as contas com lançamento na janela. Cada achado vira uma linha em `audit_log` e o evento de log `IntegrityViolationDetected` ([documento de arquitetura 0026](../03-principios-e-decisoes/documento-arquitetura/0026-conferencia-de-integridade-com-trava-por-modo.md)) |
| `PruneIdempotencyKeysHandler`, `RewrapAccountsHandler`, `RecordKeyActivationHandler` | Repetem a poda até um lote sair incompleto, recifram sob trava e observam o uso das versões de chave, e gravam `keys.version_activated` uma vez por versão ativa |
| `PostgresIntegritySessions`, `PostgresIntegritySession` (`Ledger.Infrastructure.Persistence.Integrity`) | Tomam a trava consultiva `727002` por modo e entregam a sessão que executa as consultas de [conferência de integridade](../06-fluxos/conferencia-de-integridade.md) |
| `PostgresIdempotencyKeyPruner`, `PostgresAccountKeyRewrapper` | O primeiro poda `idempotency_keys` e `account_creation_keys` com a mesma retenção. O segundo toma a trava `727003` por passagem, seleciona lotes com `FOR NO KEY UPDATE SKIP LOCKED` e atualiza as colunas do documento em `accounts`, que não é imutável ([rotação das chaves](../06-fluxos/rotacao-de-chaves.md)) |
| `PostgresAuditTrail`, `HolderDocumentProtector`, `ReloadingKeyProvider` | Gravam as linhas de auditoria do Worker, recifram o documento e entregam as versões vivas das chaves, lidas de uma pasta de segredos |

## Comandos e saúde

Os dois comandos usam o mesmo `Program`. O `MigrateCommand` chama a porta `IDatabaseMigrator`, que o `MigrationRunner` implementa: aplica os scripts SQL embutidos no assembly, cada um na sua transação, sob a trava consultiva `727001` com prazo de `Migrations:LockTimeoutSeconds`. O `InspectAccountCommand` chama o `InspectAccountIntegrityHandler` e lista, uma linha por achado, as verificações que a conta reprova. A saúde é a única superfície HTTP do Worker ([Saúde e observabilidade](../08-resiliencia-e-operacao/saude-e-observabilidade.md)).

```mermaid
---
config:
  flowchart:
    wrappingWidth: 260
    nodeSpacing: 30
    rankSpacing: 50
---
flowchart TB
    plantao("<b>Plantão da plataforma</b><br/><i>[Pessoa]</i>")
    orq["<b>Orquestrador ou balanceador</b><br/><i>[Sistema externo]</i><br/>Sondas de liveness e de readiness"]

    subgraph host ["Ledger.Worker: comandos e saúde"]
        direction TB
        mig["<b>MigrateCommand</b><br/><i>[Comando]</i><br/>--migrate. Saída 0, 1, 2 ou 3"]
        ins["<b>InspectAccountCommand</b><br/><i>[Comando]</i><br/>--inspect-account=guid. Saída 0, 1 ou 3"]
        hea["<b>HealthEndpoints do Worker</b><br/><i>[Health Checks, porta 8081]</i><br/>GET e HEAD em /health/live e /health/ready"]
    end

    subgraph app ["Ledger.Application"]
        direction TB
        hins["<b>InspectAccountIntegrityHandler</b><br/><i>[Handler]</i><br/>Confere uma conta sob demanda"]
        portas["<b>Portas</b><br/><i>[Interfaces]</i><br/>IDatabaseMigrator e IIntegritySessions"]
        hins --> portas
    end

    subgraph infra ["Ledger.Infrastructure"]
        direction TB
        run["<b>MigrationRunner</b><br/><i>[DbUp]</i><br/>Scripts embutidos, um por transação, sob a trava 727001"]
        sess["<b>PostgresIntegritySessions</b><br/><i>[Npgsql, Dapper]</i><br/>Sessão de conferência sem trava de modo"]
        chk["<b>Verificações do Worker</b><br/><i>[IHealthCheck]</i><br/>heartbeats, postgres, schema, shutdown, outbox-lag, key-usage, broker-circuit e rabbitmq"]
        est["<b>Estado de saúde</b><br/><i>[Singletons]</i><br/>WorkerHeartbeat, OutboxStatsHolder, KeyUsageHolder e o estado do circuito"]
        chk --> est
    end

    db[("<b>PostgreSQL 16</b><br/><i>[Container]</i>")]
    broker(["<b>RabbitMQ 3.13</b><br/><i>[Container]</i>"])

    plantao -->|"Linha de comando"| mig
    plantao -->|"Linha de comando"| ins
    orq -->|"HTTP"| hea
    mig -->|"MigrateAsync"| portas
    ins -->|"HandleAsync"| hins
    portas -.->|"IDatabaseMigrator"| run
    portas -.->|"IIntegritySessions"| sess
    hea -->|"Executa live e ready"| chk
    run -->|"DDL, papel ledger_migrator<br/><i>Npgsql, DbUp</i>"| db
    sess -->|"SELECT somente leitura"| db
    chk -->|"SELECT 1 e versão do esquema, com prazo de 1 s"| db
    chk -.->|"Sonda a conexão, Degraded se falhar<br/><i>AMQP</i>"| broker

    classDef component fill:#85bbf0,stroke:#3b6ea5,color:#000000
    classDef external fill:#6b6b6b,stroke:#4a4a4a,color:#ffffff
    classDef container fill:#2c6fbb,stroke:#1d4f87,color:#ffffff
    classDef person fill:#08427b,stroke:#052e56,color:#ffffff
    class mig,ins,hea,hins,portas,run,sess,chk,est component
    class orq external
    class db,broker container
    class plantao person
    style host fill:none,stroke:#1168bd,stroke-width:2px,stroke-dasharray:6 4
    style app fill:none,stroke:#1168bd,stroke-width:2px,stroke-dasharray:6 4
    style infra fill:none,stroke:#1168bd,stroke-width:2px,stroke-dasharray:6 4
```

| Componente | Responsabilidade |
|---|---|
| `MigrateCommand`, `InspectAccountCommand` (`Ledger.Worker`) | O primeiro traduz o `MigrationStatus` em código de saída: 0 (aplicada ou nada a aplicar), 1 (um script falhou), 2 (banco inalcançável ou trava ocupada além do prazo) e 3 (configuração inválida). O segundo lê `--inspect-account=<guid>`, confere a conta e sai com 0 (sem achado), 1 (com achado) ou 3 (identificador ou configuração inválidos) |
| `HealthEndpoints` do Worker | `GET /health/live`, com a verificação `heartbeats`, e `GET /health/ready`, com as marcadas como `ready`. Só GET e HEAD, e o HEAD responde sem corpo. `Unhealthy` devolve 503 com `Retry-After` |
| `InspectAccountIntegrityHandler` | Abre uma sessão e confere uma conta, sem tomar a trava de modo |
| `MigrationRunner` (`Ledger.Infrastructure.Persistence`) | Aplica os scripts embutidos que ainda faltam, na ordem, e registra cada um no diário `schemaversions`, como descreve a [migração do esquema](../06-fluxos/migracao-do-esquema.md) |
| Verificações do Worker | `heartbeats` falha se um laço ficar sem batimento além do limite (`Resilience:Health`). Na readiness, `postgres`, `schema` e `shutdown` derrubam a instância, e `outbox-lag`, `key-usage`, `broker-circuit` e `rabbitmq` só a degradam, sem tirar o 200 |

## O que o desenho mostra

O Worker é o único container que fala com o broker, e é por isso que a queda do RabbitMQ não chega à escrita. O circuit breaker do Polly fica em volta da publicação e só existe aqui ([documento de arquitetura 0011](../03-principios-e-decisoes/documento-arquitetura/0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md), [documento de arquitetura 0029](../03-principios-e-decisoes/documento-arquitetura/0029-publicador-do-outbox-com-circuito-por-mensagem.md)). A publicação sai da caixa de saída gravada junto do lançamento ([documento de arquitetura 0005](../03-principios-e-decisoes/documento-arquitetura/0005-transacao-unica-com-outbox.md)), a entrega é pelo menos uma vez, em lotes, e o prazo de reivindicação funciona como um aluguel que expira sozinho ([documento de arquitetura 0008](../03-principios-e-decisoes/documento-arquitetura/0008-rabbitmq-entrega-ao-menos-uma-vez.md)): se o processo morrer entre publicar e marcar, a mensagem sai de novo na volta seguinte e quem consome deduplica pelo `message_id`. A fila de retenção e o `mandatory` evitam que um evento sem rota se perca ([documento de arquitetura 0033](../03-principios-e-decisoes/documento-arquitetura/0033-fila-de-retencao-e-publicacao-mandatory.md)).

A conferência de integridade só lê, com uma trava por modo ([documento de arquitetura 0026](../03-principios-e-decisoes/documento-arquitetura/0026-conferencia-de-integridade-com-trava-por-modo.md)), e a correção de um achado entra como novo lançamento, não como edição, porque o ledger é só de inserção ([documento de arquitetura 0002](../03-principios-e-decisoes/documento-arquitetura/0002-ledger-imutavel-somente-insercao.md)). A recifragem só toca `accounts`, que não é imutável ([documento de arquitetura 0010](../03-principios-e-decisoes/documento-arquitetura/0010-seguranca-jwt-e-criptografia-de-pii.md)), e o mesmo executável serve de ferramenta de migração, com um papel de banco diferente do de operação ([documento de arquitetura 0007](../03-principios-e-decisoes/documento-arquitetura/0007-postgresql-npgsql-dapper-dbup.md)). A readiness do Worker degrada, mas continua em 200, quando o broker cai, porque tirar o processo do ar só atrasaria a volta. Traces, métricas e logs saem como na API ([documento de arquitetura 0012](../03-principios-e-decisoes/documento-arquitetura/0012-observabilidade-serilog-opentelemetry.md)).

## O que fica fora

Ficam fora a telemetria (`Observability`), que todo laço usa, o `ShutdownHealthCheck` e o desligamento ordenado, descritos em [encerramento e reinício](../06-fluxos/encerramento-e-reinicio.md), e o comportamento do Worker quando o banco falha, em [falha do banco de dados](../06-fluxos/falha-do-banco.md). A API tem desenho próprio em [componentes da API](nivel-3-componentes-api.md).
