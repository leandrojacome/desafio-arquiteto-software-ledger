# Políticas de resiliência

O ledger promete não perder nem duplicar dinheiro quando o banco, o broker ou a rede falham. A página reúne o que sustenta a promessa: quanto cada camada espera, o que se repete e o que não, como cada falha do banco vira resposta HTTP, como os pools se dividem e quando o publicador do outbox para de tentar. Os valores vêm da configuração, citados com a chave e com o padrão do `appsettings.json` da API, o do Worker ou o da classe de opções. Nenhum deles foi calibrado com carga em escala ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)).

O comportamento resultante, cenário por cenário, está em [Cenários de falha 1 a 8](cenarios-de-falha.md) e [9 a 16](cenarios-de-falha-9-a-16.md), e a cadeia que protege o banco antes de tudo isso, em [Limites de taxa e de concorrência](limites-de-taxa-e-concorrencia.md). A decisão de base é o [documento de arquitetura 0011](../03-principios-e-decisoes/documento-arquitetura/0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md), complementado pelos documentos de arquitetura [0027](../03-principios-e-decisoes/documento-arquitetura/0027-limites-de-taxa-e-de-concorrencia-em-cadeia.md) e [0029](../03-principios-e-decisoes/documento-arquitetura/0029-publicador-do-outbox-com-circuito-por-mensagem.md).

Quatro ideias guiam os valores abaixo. Cada timeout é menor que o de quem o envolve, para o mais interno estourar primeiro. Só se repete o que é idempotente: a escrita tem `Idempotency-Key` obrigatória, então repetir a transação inteira é seguro, e a leitura nunca é repetida pelo servidor. Pool esgotado, lock demorado e comando lento sinalizam sobrecarga, que a repetição agravaria, então viram `503` na hora. E broker, fonte de chaves e telemetria ficam fora do caminho do lançamento.

## Timeouts em camadas

| Camada | Chave de configuração | Padrão | Ao estourar |
|---|---|---|---|
| Requisição inteira | `Resilience:RequestTimeoutSeconds` | 3 s | O trabalho é cancelado e a API responde `503` com `Retry-After` de `Resilience:ServiceUnavailableRetryAfterSeconds` (1 s). As rotas de saúde ficam fora |
| Obter conexão do pool | `Postgres:Sources:{fonte}:ConnectionTimeoutSeconds` | 1 s (API), 5 s (Worker e migração) | `503` na API, sem retentativa |
| Espera pelo lock da linha | `Postgres:Sources:{fonte}:LockTimeoutMs` | 1.000 ms (escrita e Worker) | `55P03`, que vira `503`, sem retentativa |
| Comando no cliente | `Postgres:Sources:{fonte}:CommandTimeoutSeconds` | 2 s (escrita), 1 s (saldo e extrato), 10 s (Worker), 300 s (migração) | O comando é cancelado e vira `503` |
| Comando no servidor | `Postgres:Sources:{fonte}:StatementTimeoutMs` | 2.500 ms (escrita), 1.500 ms (saldo e extrato), 10.000 ms (Worker), 300.000 ms (migração) | Rede de segurança se o cancelamento do cliente não chegar |
| Sessão parada dentro de transação | `Postgres:Sources:{fonte}:IdleInTransactionTimeoutMs` | 5.000 ms (API), 15.000 ms (Worker), 300.000 ms (migração) | O servidor derruba a sessão e solta o lock (`25P03`) |
| Sonda de readiness | `Resilience:Health:ProbeTimeoutSeconds` | 1 s | A readiness responde `503` |
| Conexão com o broker | `RabbitMq:ConnectTimeoutSeconds` | 2 s | A verificação `rabbitmq` fica `Degraded` e o publicador tenta de novo com recuo |
| Confirmação do lote publicado | `Outbox:ConfirmTimeoutSeconds` | 5 s | As mensagens sem confirmação voltam ao outbox |
| Reivindicação do lote | `Outbox:LeaseSeconds` | 30 s | Outra instância reivindica o lote |
| Encerramento do processo | `Resilience:ShutdownTimeoutSeconds` | 30 s | O host para, depois de terminar o lote do outbox em curso |
| Trava da migração | `Migrations:LockTimeoutSeconds` | 120 s | A migração sai com o código 2 |

A ordem desenhada é lock (1 s) abaixo do comando (2 s), abaixo do `statement_timeout` (2,5 s), abaixo da requisição inteira (3 s), e é um acordo entre os padrões. A subida valida cada valor contra a sua faixa e que `MinPoolSize` não passa de `MaxPoolSize`, mas não compara um timeout com o outro, então uma configuração que inverta a ordem sobe sem aviso (um validador da hierarquia está em [Evolução futura](../11-evolucao/evolucao-futura.md)). A relação entre cada limite de concorrência e o pool correspondente também é validada, e está em [Limites de taxa e de concorrência](limites-de-taxa-e-concorrencia.md). Se mais de 1% das escritas precisarem de retentativa, é sinal de contenção e não de falha transitória, e mais tentativas só agravariam ([documento de arquitetura 0011](../03-principios-e-decisoes/documento-arquitetura/0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md)).

## Retentativa da escrita

A retentativa é um `ResiliencePipeline` do Polly 8 que envolve só a unidade de trabalho da escrita (`PostgresUnitOfWork`). Cada tentativa abre conexão e transação novas, reserva a chave de idempotência e segue os passos do [Fluxo: registro de lançamento](../06-fluxos/registro-de-lancamento.md), de modo que repetir a função inteira é seguro mesmo quando o desfecho do commit anterior é desconhecido.

São 2 retentativas (três tentativas no total, faixa de 0 a 5, `Resilience:Retry:MaxRetryAttempts`) com atraso inicial de 50 ms (faixa de 10 a 1.000, `Resilience:Retry:BaseDelayMs`), recuo exponencial e jitter. O prazo da requisição limita o tempo total e chega a cada tentativa como `CancellationToken`. Esgotadas as tentativas, a resposta é `503` com `Retry-After`. Cada retentativa registra o evento 1006 `TransientDatabaseFailure`, com SQLSTATE e tentativa, e incrementa `ledger_db_retries_total` com `reason` igual a `deadlock`, `serialization_failure` ou `transient_connection`.

## Classificação das falhas do banco

Toda falha de infraestrutura vira `503` com `Retry-After`, nunca `500`. A tabela cruza duas decisões: se a unidade de trabalho repete a transação (`PostgresRetryPredicate`) e como a falha final é respondida (`PostgresTransientFailureClassifier`, consultado pelo `GlobalExceptionHandler`).

| SQLSTATE ou condição | Significado | Repete | Resposta | Log |
|---|---|---|---|---|
| Classe `08`, ou `NpgsqlException` transitória que não seja timeout | Conexão caída, recusada ou fechada | Sim | `503` depois das tentativas | 1006 em cada tentativa, 9002 no fim |
| `57P01` | Desligamento administrativo | Sim | `503` | 1006, 9002 |
| `57P03` | O servidor ainda não aceita conexões | Sim | `503` | 1006, 9002 |
| `25P03` | Sessão derrubada por `idle_in_transaction_session_timeout`; nada foi confirmado | Sim | `503` se persistir | 1006, 9002 |
| `40P01`, `40001` | Deadlock, falha de serialização | Sim | `503` se persistir | 1006, 9002 |
| `55P03` | `lock_timeout` na linha da conta | Não | `503` com `Retry-After` | 1007, 9002 |
| `57014`, ou estouro do `Command Timeout` do cliente | Comando cancelado | Não | `503` | 9002 |
| `53300`, ou espera de 1 s por conexão do pool | Saturação | Não | `503` | 9002 |
| `53100` | Disco cheio | Não | `503` | 9002 |
| `25006` | Transação somente leitura: o nó está em promoção ou foi rebaixado | Não | `503` | 9002 |
| `57P02` | Desligamento anormal de outro processo do servidor | Não | `503` | 9002 |
| `23xxx` em restrição que o desenho considera impossível | Defeito | Não | `500` `INTERNAL_ERROR` | 1008 (`Error`) |
| Cancelamento do chamador (`RequestAborted`) | O chamador foi embora | Não | `499`, sem corpo | Só o resumo da requisição |
| Qualquer outra exceção | Inesperado | Não | `500` `INTERNAL_ERROR` | 9001 (`Error`) |

Não se repetem `55P03`, `57014`, `53300`, `53100`, `25006` nem pool esgotado porque repetir não muda o estado do outro lado, e em saturação agravaria o problema. Repetir `25006` em particular não ajuda: a promoção de um nó leva segundos, e um recuo de 50 ms não a atravessa.

O evento 9002 traz o `SqlState` (cinco caracteres, sem dado nenhum) para o plantão distinguir, pelo log, banco fora, em promoção e sem disco. O corpo da resposta nunca traz pilha, texto de SQL nem nome de servidor ou de banco. O classificador do `GlobalExceptionHandler` também trata `KeyProviderUnavailableException` como transitória, e é por isso que a criação de conta responde `503` com a fonte de chaves fora ([Catálogo de erros](../05-contratos/catalogo-de-erros.md)). `PostgresRetryPredicateTests` cobre quais falhas repetem, `PostgresTransientFailureClassifierTests`, quais viram `503` e quais são defeito, e `PostgresUnitOfWorkRetryTests`, a repetição, a desistência na terceira tentativa e o registro de cada uma.

## Pools de conexão

Há cinco fontes de dados, cada uma com o seu pool e o seu papel de banco. Separar saldo e extrato, e não só leitura e escrita, impede que um extrato lento consuma as conexões do saldo, a rota de maior volume. A conta que dimensiona os pools está em [Capacidade e escala](capacidade-e-escala.md), e os papéis em [Modelo de dados](../05-contratos/modelo-de-dados.md).

| Fonte | `Application Name` | Pool máximo | Pool mínimo | Somente leitura | Usada por |
|---|---|---|---|---|---|
| `Write` | `ledger-api-write` | 7 | 2 | Não | Escrita da API |
| `Balance` | `ledger-api-balance` | 8 | 2 | Sim | Saldo atual e em um instante |
| `Statement` | `ledger-api-statement` | 4 | 1 | Sim | Extrato e sonda de readiness da API |
| `Worker` | `ledger-worker` | 5 | 1 | Não | Publicador, podas, medição, conferência e recifragem |
| `Migrator` | `ledger-migrator` | 2 | 0 | Não | Só o comando `--migrate` |

Em todas as fontes valem `Multiplexing=false` (há transações explícitas), `Tcp Keepalive=true`, `Connection Idle Lifetime=300`, `Connection Pruning Interval=10`, `Max Auto Prepare=20` com `Auto Prepare Min Usage=2`, GSS desligado (o ledger não usa Kerberos) e `Include Error Detail` falso fora do desenvolvimento. Os parâmetros de servidor entram na palavra-chave `Options`: `timezone=UTC`, `lock_timeout`, `statement_timeout`, `idle_in_transaction_session_timeout` e, nas fontes de leitura, `default_transaction_read_only=on`, de modo que um `UPDATE` num caminho de consulta seria recusado pelo banco. Fora de `Development` e `Testing`, a subida exige `SslMode` igual a `VerifyFull`. Cada fonte, exceto a migração, abre os seus `MinPoolSize` conexões na subida, e uma falha nisso gera o evento 5031 em `Warning` sem impedir o processo de subir.

O pool de escrita é pequeno de propósito, porque também limita a concorrência no banco. O risco é uma conta quente em rajada ocupar conexões esperando o lock e atrasar escritas de outras contas na mesma instância. Se a medição mostrar isso, o primeiro ajuste é subir o pool de 7 para 12 por instância, o que ainda cabe em `max_connections` ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)).

O banco do Compose sobe com `max_connections=200`, `synchronous_commit=on` e `tcp_keepalives` de 10, 5 e 3, que recolhem em cerca de 25 s a sessão de um cliente que sumiu sem fechar a conexão. O servidor de produção é responsabilidade da plataforma de banco.

## Circuit breaker do broker

O breaker existe só no Worker, ao redor da publicação, e conta mensagens, não lotes ([documento de arquitetura 0029](../03-principios-e-decisoes/documento-arquitetura/0029-publicador-do-outbox-com-circuito-por-mensagem.md)). Os parâmetros são a fração de falhas que o abre (`Resilience:BrokerCircuitBreaker:FailureRatio`, 0,5), a janela de amostragem (`SamplingSeconds`, 30 s), o mínimo de publicações na janela (`MinimumThroughput`, 10) e o tempo aberto antes de testar (`BreakSeconds`, 30 s).

Contam como falha a indisponibilidade, o estouro do prazo de confirmação e o `nack`. Não contam o erro do próprio ledger ao montar o evento (`serialization`) nem a devolução por falta de fila (`unroutable`), porque o broker respondeu. Com o circuito fechado e a conexão pronta o Worker reivindica o lote cheio (`Outbox:BatchSize`, 200), e em qualquer outro estado, nada. Para reabrir, ele manda a cada volta uma sonda vazia, com confirmação, na chave `ledger.probe` e prazo de 5 s. O estado é o gauge `broker_circuit_breaker_state` (0 fechado, 1 meio aberto, 2 aberto), e cada mudança gera o evento 3003.

## Publicação e reconexão

A reivindicação é uma instrução com `FOR UPDATE SKIP LOCKED` que grava `locked_until` (agora mais `Outbox:LeaseSeconds`) e incrementa `attempts`. A publicação acontece fora de transação, espera as confirmações e termina com duas instruções em lote: uma marca as confirmadas e outra devolve a tentativa das que nem chegaram a ser tentadas (indisponibilidade, prazo ou falta de fila). As duas rodam com prazo próprio, igual ao da reivindicação, para o desligamento e um lote lento não perderem a marcação ([documento de arquitetura 0030](../03-principios-e-decisoes/documento-arquitetura/0030-prazo-proprio-para-marcar-as-mensagens-publicadas.md)). Os SQL estão em [Fluxo: publicação do outbox](../06-fluxos/publicacao-do-outbox.md).

| Parâmetro | Chave | Padrão |
|---|---|---|
| Tamanho do lote | `Outbox:BatchSize` | 200 |
| Espera quando o lote vem vazio | `Outbox:IdlePollMs` | 200 ms |
| Tentativas que chamam um humano | `Outbox:FailedAttempts` | 5 |
| Janela de mensagens examinada para esse limiar | `Outbox:FailedHeadWindow` | 1.000 |
| Recuo mínimo e máximo da reconexão ao broker | `RabbitMq:ReconnectMinSeconds` e `ReconnectMaxSeconds` | 1 s e 30 s, com jitter de `RabbitMq:ReconnectJitterPercent` (20%) |

A reconexão é do próprio ledger, com recuo exponencial entre o mínimo e o máximo, porque a recuperação automática da biblioteca tem intervalo fixo. A devolução por falta de fila marca a topologia como desatualizada e provoca uma reconexão que a declara de novo. Uma mensagem que o broker nunca aceita continua sendo reivindicada e nunca é apagada, porque apagar depois de N tentativas perderia dinheiro sem ninguém ver. A partir do limiar, o evento 3005 e o gauge `outbox_failed_messages` chamam um humano.

## Laços do Worker e encerramento

Os laços de publicação, medição do outbox, poda do outbox, poda das chaves de idempotência e recifragem capturam a exceção de uma volta, registram o evento 3008 `WorkerLoopFailed`, incrementam `ledger_worker_loop_failures_total` e tentam de novo depois de um recuo exponencial de 1 a 30 s (`Worker:FailureBackoff`, jitter de 20%) que volta ao mínimo na primeira volta bem-sucedida. Os dois laços da conferência de integridade registram o evento 4003, incrementam o mesmo contador e esperam o intervalo da próxima execução, sem recuo exponencial. O processo nunca encerra por falha de um laço, porque reiniciar um Worker com o banco fora não ajudaria o banco e derrubaria o publicador no instante em que ele volta.

No `SIGTERM`, o host espera até 30 s pelas requisições em curso e pelos serviços de fundo. O publicador termina o lote em curso, publicado e marcado, com prazo de drenagem igual ao tempo de encerramento menos 5 s de margem (no mínimo 1 s), e não reivindica outro. O Compose dá 35 s de tolerância (`stop_grace_period`), e as verificações `shutdown` da API e do Worker passam a `Unhealthy` assim que a parada começa ([Fluxo: encerramento e reinício](../06-fluxos/encerramento-e-reinicio.md)).
