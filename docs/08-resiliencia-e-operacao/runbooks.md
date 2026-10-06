# Procedimentos de operação

O que fazer quando um alerta dispara: como diagnosticar, agir, confirmar que voltou ao normal e quando chamar mais gente. Há um procedimento para cada família de alertas de [Indicadores, objetivos e alertas](slos-e-alertas.md), e o nome de cada seção é o que a coluna Procedimento daquela página cita. O repositório não traz as regras de alerta (a pilha de monitoração é externa), só os sinais que elas consomem, e os comandos e as consultas abaixo existem e se comportam como descrito. O que cada falha significa para o dinheiro e para o chamador está em [Cenários de falha 1 a 8](cenarios-de-falha.md) e [9 a 16](cenarios-de-falha-9-a-16.md).

Os comandos são para o ambiente do Compose, o único que o repositório sobe, e em `sh`. No PowerShell, troque as aspas dos filtros do `jq`. Em outro ambiente valem os mesmos passos, com os comandos do orquestrador em uso.

## Antes de começar

As consultas se conectam por um cliente `psql` com o papel indicado. O `ledger_readonly` lê `ledger_entries`, `account_balances`, `idempotency_keys` e `audit_log`, e não lê `outbox_messages` nem as colunas do documento do titular. As consultas de `outbox_messages` e de `pg_stat_activity` pedem um papel administrativo, de quem opera o banco, e as credenciais do `ledger_worker` pertencem ao Worker e não servem para consulta manual.

```bash
PGPASSWORD="$LEDGER_READONLY_PASSWORD" psql -h localhost -p "$POSTGRES_PORT" -U ledger_readonly -d ledger
```

Estes comandos respondem a primeira pergunta de qualquer procedimento:

```bash
docker compose ps
curl -s http://localhost:8080/health/ready
curl -s http://localhost:8081/health/ready
docker compose logs --since 15m api worker --no-log-prefix | jq -cR 'fromjson? | select(."@l" == "Warning" or ."@l" == "Error" or ."@l" == "Fatal")'
docker compose logs api worker --no-log-prefix | jq -cR 'fromjson? | select(.EventId.Id == 9002)'
docker compose logs api worker --no-log-prefix | jq -cR 'fromjson? | select(.CorrelationId == "COLE-O-IDENTIFICADOR")'
```

Os dois `curl` são a readiness da API e a do Worker, que respondem só o estado agregado. O primeiro `jq` lista avisos e erros (o nível `Information` não tem o campo `@l`), o segundo filtra por evento, cujos identificadores estão em [Saúde e observabilidade](saude-e-observabilidade.md), e o terceiro segue uma requisição de ponta a ponta pelo `X-Correlation-Id`.

Quatro regras valem em todos os procedimentos. Nenhum faz `UPDATE` nem `DELETE` em `ledger_entries`: erro se corrige com estorno ou com lançamento de ajuste. Repetir uma escrita com a mesma `Idempotency-Key` é sempre seguro. Não se reinicia todas as instâncias de uma vez, porque isso gera uma manada de reconexões contra um banco que pode estar voltando. E não se sobe a API para resolver latência antes de saber se o gargalo é o banco, porque mais instâncias disputando a mesma linha de conta pioram o p99.

## Outbox sem drenar

Vale para os alertas `OutboxOldestPendingAgeHigh`, `OutboxOldestPendingAgeCritical`, `OutboxBacklogLarge`, `OutboxFailedMessages`, `BrokerDisconnected`, `BrokerCircuitOpen` e `OutboxUnroutable`. Sem pilha de monitoração, o sinal é a readiness do Worker em `Degraded` pela verificação `outbox-lag`, que degrada quando a pendente mais antiga passa de 60 s. Eventos de lançamentos já confirmados não estão chegando ao broker. Nenhum dinheiro está em risco, porque lançamento e evento são gravados na mesma transação, e o que atrasa é quem depende dos eventos.

**Diagnóstico.** `Degraded` na readiness aponta o broker, o circuito ou o atraso, mas não diz qual. Veja qual sinal está ativo: `broker_connected` igual a 0 (sem conexão), `broker_circuit_breaker_state` igual a 2 (circuito aberto), `outbox_publish_failures_total` crescendo, por `reason`, ou `worker_last_cycle_timestamp_seconds` com `loop="outbox"` parado. Procure os eventos 3007 (conexão), 3003 (circuito), 3009 (sem fila), 3002 (falha de publicação) e 3005 (mensagem presa):

```bash
docker compose logs --since 15m worker --no-log-prefix | jq -cR 'fromjson? | select(.EventId.Id == 3007 or .EventId.Id == 3003 or .EventId.Id == 3009 or .EventId.Id == 3002 or .EventId.Id == 3005)'
```

Dimensione o acúmulo, com papel administrativo:

```sql
SELECT count(*) AS pendentes, min(created_at) AS mais_antiga
FROM outbox_messages
WHERE published_at IS NULL;
```

Olhe o broker. Um alarme de disco ou de memória bloqueia as conexões que publicam sem gerar erro, só silêncio, e as duas últimas consultas mostram a fila de retenção e a ligação dela com a troca `ledger.events`:

```bash
curl -s -u "$RABBITMQ_USERNAME:$RABBITMQ_PASSWORD" http://localhost:15672/api/health/checks/alarms
curl -s -u "$RABBITMQ_USERNAME:$RABBITMQ_PASSWORD" http://localhost:15672/api/queues/%2F/retention.ledger.entry-registered
curl -s -u "$RABBITMQ_USERNAME:$RABBITMQ_PASSWORD" http://localhost:15672/api/bindings/%2F/e/ledger.events/q/retention.ledger.entry-registered
```

**Ação.** Sem conexão ou com o circuito aberto, restabeleça o broker (é da plataforma): o Worker reconecta sozinho, com recuo de 1 a 30 s, e a sonda fecha o circuito. Não reinicie o Worker de dez em dez minutos para ajudar, porque isso só zera o estado do circuito e adia a drenagem. Com o Worker parado (`WorkerStalled`), siga [Worker parado ou laço falhando](#worker-parado-ou-laço-falhando).

Sem fila (`unroutable`, evento 3009), a fila de retenção foi apagada ou nunca existiu, e a reconexão seguinte a declara de novo. Com `RabbitMq:Retention:Enabled=false`, as mensagens esperam no outbox sem gastar tentativa até um consumidor declarar a sua fila.

Com mensagens de 5 tentativas ou mais (`OutboxFailedMessages`, evento 3005), o broker recusa a mensagem (`nack`) ou o ledger não consegue montá-la (`serialization`). Ela nunca é apagada e é reivindicada de novo a cada 30 s. Liste as presas e abra um incidente com o `id` e o motivo do evento 3002:

```sql
SELECT id, account_id, type, attempts, created_at, locked_until
FROM outbox_messages
WHERE published_at IS NULL AND attempts >= 5
ORDER BY created_at
LIMIT 20;
```

**Confirmação e escalonamento.** `outbox_oldest_pending_age_seconds` abaixo de 60 e caindo, `outbox_pending_messages` indo a zero, `broker_connected` em 1 e `broker_circuit_breaker_state` em 0. O tempo de drenagem é o número de pendentes dividido pela diferença entre a taxa de `outbox_published_total` e a de chegada de lançamentos. Acione o time de banco se o espaço livre cair abaixo de 20% ou se a idade passar de uma hora, porque o outbox acumula na taxa de escrita e a hora do pico premissado soma alguns gigabytes ([Capacidade e escala](capacidade-e-escala.md)).

## Latência de escrita alta

Vale para `LedgerWriteLatencyHigh`, `LedgerWriteLatencyCritical`, `LedgerBalanceLatencyHigh` e `DbRetryRateHigh`: o p99 da rota passou do orçamento, 150 ms na escrita e 50 ms no saldo.

**Diagnóstico.** Veja em qual rota e com qual status a latência subiu no histograma HTTP, e se `ledger_entry_duration_seconds` sobe junto, por `outcome`: se só a rota sobe, o tempo está fora do caso de uso (autenticação, validação, fila de concorrência). A escrita não emite a duração dos comandos SQL, então o tempo se localiza de outro jeito. Toda resposta `2xx` acima de 150 ms é registrada em `Warning` com o `TraceId`, e com ele se abre o trace, em que os spans de comando do Npgsql mostram qual instrução demorou:

```bash
docker compose logs --since 10m api --no-log-prefix | jq -cR 'fromjson? | select(."@l" == "Warning" and .StatusCode != null) | {rota: .RequestRoute, ms: .Elapsed, trace: .TraceId}'
```

No banco, `pg_stat_statements`, se ativo, e o atraso de replicação indicam o custo do commit, que na topologia de produção inclui a confirmação do standby. Procure uma conta quente com o `ledger_readonly`, lembrando que mais de 15.000 lançamentos em 5 minutos passa de 50 por segundo, e depois quem a martela:

```sql
SELECT account_id, count(*) AS lancamentos
FROM ledger_entries
WHERE recorded_at > now() - interval '5 minutes'
GROUP BY account_id
ORDER BY lancamentos DESC
LIMIT 5;

SELECT client_id, count(*) AS lancamentos
FROM ledger_entries
WHERE account_id = :'account_id' AND recorded_at > now() - interval '5 minutes'
GROUP BY client_id
ORDER BY lancamentos DESC;
```

Veja quem bloqueia quem, com papel administrativo, e o pool (`db_client_connection_npgsql_pending_requests`, `db_client_connection_npgsql_timeouts_total` e os eventos 1007, 6101 e 6102):

```sql
SELECT pid,
       now() - query_start AS esperando,
       wait_event_type,
       wait_event,
       pg_blocking_pids(pid) AS bloqueado_por,
       left(query, 80) AS consulta
FROM pg_stat_activity
WHERE cardinality(pg_blocking_pids(pid)) > 0;
```

**Ação.** Se o bloqueador for uma sessão manual esquecida com transação aberta, `pg_cancel_backend(pid)` resolve e é seguro, porque a transação desfaz tudo e não existe escrita parcial. Se for uma conta quente legítima, fale com o dono do `client`, peça que contas internas muito usadas sejam espalhadas em várias e, se preciso, aperte `RateLimiting:WritePerAccount`. Se `DbRetryRateHigh` disparou, mais de 1% das escritas precisam de retentativa, o que indica contenção e não falha transitória, e mais retentativas só agravariam ([documento de arquitetura 0011](../03-principios-e-decisoes/documento-arquitetura/0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md)). Só aumente instâncias da API se `db_client_connection_npgsql_pending_requests` estiver em zero e a CPU da API estiver saturada.

**Confirmação e escalonamento.** O p99 abaixo do orçamento por 15 minutos e a taxa de retentativas abaixo de 1%. Time de banco, se o p99 passar de 500 ms por mais de 15 minutos depois dessas ações.

## Erros 5xx

Vale para `LedgerAvailabilityFastBurn`, `LedgerAvailabilitySlowBurn` e `LedgerAvailabilityBudgetDrift`: mais de 0,72% das requisições (ou 0,3%, ou 0,05%, conforme o alerta) voltam `5xx`.

**Diagnóstico.** Separe por rota e por status:

```text
sum by (http_route, http_response_status_code) (rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]))
```

Leia o tipo de falha nos logs. `503` com o evento 6102 é saturação de concorrência e segue [Saturação e limites](#saturação-e-limites). `503` com o 9002 é dependência indisponível, e o `SqlState` diz qual: segue [Banco indisponível ou em failover](#banco-indisponível-ou-em-failover). `500` com o 9001 (`Error`) é defeito do código, e vale anotar o `CorrelationId` e o `TraceId` da linha. Confirme o estado de cada instância em `/health/ready` e veja se houve implantação recente.

**Ação.** Se houve implantação recente, o primeiro passo é voltar a aplicação para a versão anterior. A readiness aceita um esquema mais novo que o código e as migrações são compatíveis para trás ([Fluxo: migração do esquema](../06-fluxos/migracao-do-esquema.md)), então o rollback não exige desfazer migração. Se é o banco em failover, espere, confirme que as instâncias se recuperam sozinhas e não reinicie todas de uma vez.

**Confirmação e escalonamento.** A fração de `5xx` abaixo do limiar nas duas janelas, e `ledger_integrity_check_runs_total` com `result="ok"` crescendo no ciclo seguinte da conferência, antes de dar o incidente por encerrado. Time de banco se a origem for o PostgreSQL, e o time da aplicação se for `500` por defeito de código.

## Banco indisponível ou em failover

Vale para `ApiReadinessFailing` e para `5xx` com o evento 9002: a readiness da API e a do Worker respondem `503` com `Retry-After: 5`, a liveness segue `200` e escritas e leituras respondem `503` em até 3 s.

**Diagnóstico.** Confirme com `curl -s -i http://localhost:8080/health/ready`, o status e o `Retry-After`, e leia o `SqlState` do evento 9002 e os eventos 9101 e 9102:

| Sinal | Significado |
|---|---|
| Classe `08`, `57P01`, `57P03` | Banco fora, reiniciando ou ainda subindo |
| `25006` | Nó em promoção ou rebaixado a réplica: a conexão aponta para o nó errado |
| `53300` | Conexões esgotadas no servidor: a soma dos pools passou de `max_connections` |
| `53100` | Disco cheio |
| `55P03`, `57014` | Lock ou comando lento: siga [Latência de escrita alta](#latência-de-escrita-alta) |
| Evento 9102 | O esquema está abaixo da versão que o código espera |

O estado de uma promoção ou de uma replicação não é visível para o ledger, então peça-o à plataforma.

**Ação.** Não reinicie a API nem o Worker: o Npgsql descarta as conexões quebradas e reconecta, e a readiness volta depois de expirar o cache de 5 s. Com banco fora ou em promoção, espere a plataforma (a promoção leva uns 30 a 90 s, estimativa que nunca foi medida). Depois de uma promoção, confira que o relógio do novo primário não está atrás do que já foi gravado, com a consulta abaixo, que lê uma tabela de 20 milhões de linhas na escala premissada, então rode uma vez:

```sql
SELECT clock_timestamp() AS agora_do_banco, (SELECT max(last_recorded_at) FROM account_balances) AS maior_recorded_at;
```

O banco corrige a ordem mesmo com o relógio atrasado, mas os lançamentos ficam à frente do tempo real até o relógio alcançar, e havendo diferença se segue [Relógio com desvio](#relógio-com-desvio).

Com conexões esgotadas, confira a soma dos pools das instâncias, do Worker e da reserva contra `max_connections` ([Capacidade e escala](capacidade-e-escala.md)). Com disco cheio, libere espaço pelo caminho de menor risco que o [cenário 13](cenarios-de-falha-9-a-16.md) descreve. Com esquema atrasado, aplique a migração (`docker compose run --rm migrator`), que sai com 0 (aplicada), 1 (um script falhou), 2 (banco inalcançável ou trava ocupada além de `Migrations:LockTimeoutSeconds`) ou 3 (configuração inválida).

**Confirmação e escalonamento.** As duas readiness em `200` em até 30 s sem reiniciar nada, `ledger_worker_loop_last_success_timestamp_seconds` voltando a avançar em todos os laços, `outbox_oldest_pending_age_seconds` caindo e a conferência com `result="ok"` no ciclo seguinte. Time de banco ou de plataforma. Com um standby só, rebaixar a replicação para assíncrona abre mão do RPO zero e é decisão de quem lidera o plantão, nunca automática.

## Saturação e limites

Vale para `DbPoolSaturated` e `RateLimitSustained`: respostas `429` e `503` com `Retry-After`, ou pedidos esperando conexão no pool.

**Diagnóstico.** Veja `ledger_rate_limit_rejections_total` por `policy`. As `*-concurrency` indicam instância saturada e respondem `503`, `write-per-client` e `read-per-client` indicam um chamador acima da cota e respondem `429`, e `write-per-account` indica conta quente. Os eventos 6101 e 6102 trazem o `ClientId`. Compare com `db_client_connection_npgsql_pending_requests` e `db_client_connection_npgsql_timeouts_total` para ver se a pressão já chegou ao pool.

**Ação.** Para cota de chamador, fale com o dono do sistema: mudar `RateLimiting:WritePerClient` ou `ReadPerClient` exige reiniciar as instâncias, uma de cada vez, e só vale com a concordância do chamador. Para concorrência saturada, aumente instâncias só se o banco tiver folga, já que cada instância da API soma 19 conexões (pools de 7, 8 e 4) e a soma de tudo precisa caber em `max_connections`. Lembre aos chamadores o recuo exponencial com variação aleatória, porque o `Retry-After` do `503` de concorrência é fixo em 1 s.

**Confirmação e escalonamento.** As recusas por segundo de volta a zero e `db_client_connection_npgsql_pending_requests` em zero. Time de banco, se o pool continuar saturado com a carga dentro do combinado.

## Autenticação e uso indevido

Vale para `AuthFailureSpike`, `IdempotencyConflictSpike`, `ClientDebitVolumeAnomaly`, `ClientInsufficientFundsProbe`, `ClientReversalVolumeAnomaly` e `BalanceNotFoundScan`: pico de falhas de autenticação ou padrão anômalo de um chamador, com mais débitos, mais recusas por saldo, mais estornos ou varredura de contas inexistentes.

**Diagnóstico.** Para falhas de autenticação, separe por motivo com `sum by (reason) (rate(ledger_auth_failures_total[5m]))`. O motivo `expired` quase sempre é um chamador que parou de renovar o token ou um relógio dessincronizado, em qualquer direção. O `invalid_token` logo depois de uma troca de chaves no emissor sugere cache de chaves antigo na API (o `JwtBearer` renova o conjunto sozinho ao ver um `kid` desconhecido, no máximo uma vez a cada cinco minutos), e o `missing_token` aponta chamador mal configurado. Para os demais alertas, o rótulo `client` diz quem é o chamador. O ledger não registra o endereço de origem das requisições, que se descobre na borda (balanceador ou gateway), cruzando o horário e o `CorrelationId`.

**Ação.** Em `expired` e `missing_token`, contate o dono do chamador. Em `invalid_token` depois de troca de chaves, espere um ciclo de cinco minutos e, se persistir, reinicie uma instância para forçar a releitura. Origem desconhecida ou numerosa é ataque, e o bloqueio acontece na borda e não no ledger. Para chamador comprometido, peça ao emissor que revogue a credencial do cliente: retirar o escopo `ledger.write` dele é o único freio, e é grosseiro, porque vale para todas as contas do chamador.

**Confirmação e escalonamento.** O alerta apagado e o padrão do `client` de volta ao normal. Segurança, se a origem não for interna ou se um sucesso vier depois de uma série de falhas da mesma origem.

## Divergência de integridade

Vale para `LedgerIntegrityViolation` e `LedgerIntegrityCheckStale`: a conferência achou divergência entre o saldo guardado, a cadeia dos lançamentos e a versão da conta, ou parou de rodar. Nada é corrigido por `UPDATE` nem `DELETE`, e nada é arrumado antes de entender a causa, porque ou o banco foi alterado por fora do caminho normal ou há um defeito de código, e as duas hipóteses são sérias.

**Diagnóstico.** O evento 4002 traz a verificação (`Check`), a conta, o lançamento e a versão do primeiro ponto inconsistente, e nunca o valor:

```bash
docker compose logs --since 1h worker --no-log-prefix | jq -cR 'fromjson? | select(.EventId.Id == 4002) | {check: .Check, conta: .AccountId, lancamento: .EntryId, versao: .AccountVersion, modo: .Mode}'
```

O alerta se apaga sozinho em 10 minutos e o achado continua registrado. A trilha é a fonte do que está em aberto, com os valores esperado e encontrado, lida com o `ledger_readonly`:

```sql
SELECT recorded_at, account_id,
       details ->> 'check' AS verificacao,
       details ->> 'mode' AS modo,
       details ->> 'accountVersion' AS versao,
       details ->> 'expected' AS esperado,
       details ->> 'found' AS encontrado
FROM audit_log
WHERE event_type = 'integrity.violation_detected'
ORDER BY recorded_at DESC
LIMIT 20;
```

Rode a conferência completa de uma conta, que soma todo o histórico dela, o que a execução periódica não faz. O comando sai com 0 (conta íntegra), 1 (achados, cada um uma linha `Error` do evento 4007) ou 3 (identificador ou configuração inválidos):

```bash
docker compose run --rm worker --inspect-account=COLE-O-GUID-DA-CONTA
```

Compare os três números da conta (rode com `psql -v account_id=<uuid>`):

```sql
SELECT b.balance AS saldo_corrente,
       COALESCE(sum(CASE e.type WHEN 'CREDIT' THEN e.amount ELSE -e.amount END), 0) AS soma_dos_lancamentos,
       (SELECT balance_after FROM ledger_entries WHERE account_id = b.account_id ORDER BY account_version DESC LIMIT 1) AS ultimo_balance_after
FROM account_balances b
LEFT JOIN ledger_entries e ON e.account_id = b.account_id
WHERE b.account_id = :'account_id'
GROUP BY b.account_id, b.balance;
```

Se o saldo corrente diverge da soma mas a cadeia de `balance_after` está íntegra, o que está errado é o dado derivado, `account_balances`. Se a cadeia está quebrada, os lançamentos foram alterados, e a verdade é o que foi registrado. Procure a causa nesta ordem: implantação recente da API ou do Worker, SQL manual (o registro de auditoria do banco mostra, quando ativo em produção) e restauração de backup ou failover recente.

**Ação.** Antes de qualquer ação, preserve o que a investigação vai precisar: os logs do período, o registro de auditoria do banco e um instantâneo do banco. Se a divergência começou depois de uma implantação, volte a versão. O ledger não congela contas, e a forma de parar novas escritas de um chamador é retirar o escopo `ledger.write` dele no emissor.

A correção depende da causa. Se só `account_balances` divergiu, ele é derivável e pode ser recalculado a partir dos lançamentos, por procedimento do administrador do banco com dupla aprovação, registrado na trilha (o repositório não traz um comando pronto para isso). Se os lançamentos foram afetados, a correção é um lançamento de ajuste ou um estorno, nunca uma edição, também com dupla aprovação.

**Confirmação e escalonamento.** A execução seguinte (em até 5 minutos para contas com movimento, em até 24 horas para uma conta parada) sem divergência, `ledger_integrity_check_runs_total` com `result="ok"` crescendo e o `--inspect-account` da conta saindo com 0. Segurança e o encarregado de dados desde o primeiro momento, diante de qualquer suspeita de acesso indevido, e não só depois de a causa estar confirmada. Para a divergência em si, o time de banco.

## Worker parado ou laço falhando

Vale para `WorkerStalled`, `WorkerMetricsAbsent` e `WorkerLoopFailing`: o batimento de um laço parou, nenhuma instância do Worker está exportando métricas, ou um laço falha ciclo após ciclo.

**Diagnóstico.** `docker compose ps worker` mostra se o contêiner reinicia, e `curl -s -i http://localhost:8081/health/live` responde `503` quando um batimento passou do limite. O evento 3008 (ou o 4003, na conferência) traz o `Loop` e o tipo da exceção:

```bash
docker compose logs --since 30m worker --no-log-prefix | jq -cR 'fromjson? | select(.EventId.Id == 3008 or .EventId.Id == 4003)'
```

Veja `ledger_worker_loop_failures_total` e `ledger_worker_loop_last_success_timestamp_seconds` por `loop`: o laço que falha sem parar continua batendo o coração, e só essas duas séries o mostram.

**Ação.** A exceção diz a causa. Se for o banco, siga [Banco indisponível ou em failover](#banco-indisponível-ou-em-failover), e se for o broker, [Outbox sem drenar](#outbox-sem-drenar). Nos demais casos, reinicie uma instância com `docker compose restart worker`. As mensagens estão no banco e o reinício é seguro: no máximo algumas serão publicadas duas vezes, e os consumidores deduplicam pelo `message_id`. Se o mesmo laço voltar a falhar com o mesmo tipo de exceção depois do reinício, é defeito, e vale reunir os logs e abrir um incidente com o time da aplicação.

**Confirmação.** `worker_last_cycle_timestamp_seconds` avançando para todos os laços e `/health/live` em `200`.

## Chaves de dados pessoais

Vale para `KeyReloadFailing` e `PiiRewrapPending`, ou readiness com `keys` ou `key-usage` em `Degraded`: a criação de conta responde `503`, a recarga das chaves falha ou a rotação não termina.

**Diagnóstico.** Os eventos 7002 (fonte indisponível), 7003 (recarga falhou), 7005 (chave rejeitada), 7009 (versão sumiu da fonte), 7010 (conta com versão mais nova que a ativa do processo) e 7011 (conta com versão que o processo não lê), e as métricas `ledger_key_reloads_total` por `result` e `ledger_pii_accounts_below_active_key`.

**Ação.** Com a fonte indisponível, restabeleça a pasta ou o armazenamento de segredos: a próxima recarga, em até `Security:Pii:ReloadMinutes` (10 minutos), restaura a criação de conta sem reiniciar nada, e lançamentos, saldo e extrato seguem funcionando, porque não dependem da chave. O evento 7005 na subida significa chave malformada ou sem prova de cifra e decifra, e o material precisa ser corrigido. O 7009 pede restaurar as versões de chave na fonte antes de qualquer outra ação, porque as contas cifradas com elas não podem ser lidas. Os 7010 e 7011 pedem alinhar `Security:Pii:ActiveKeyVersion` e os conjuntos de chaves entre todas as instâncias da API e do Worker. Numa rotação em andamento, `ledger_pii_accounts_below_active_key` chega a zero quando todas as contas foram recifradas, e só então a versão antiga pode sair.

**Confirmação e escalonamento.** A criação de conta de volta a `201`, `ledger_key_reloads_total` com `result="ok"` crescendo e o gauge de contas abaixo da chave ativa em zero ao fim de uma rotação. Segurança, se houver suspeita de vazamento de chave, caso em que a rotação de emergência está em [Proteção de dados](../07-consistencia-e-seguranca/protecao-de-dados.md).

## Relógio com desvio

Vale para `RecordedAtCorrectionsDetected`, `5xx` ou `401` intermitentes em uma instância só, ou erro de chamador que some quando repete: o banco corrigiu o `recorded_at` de lançamentos porque o relógio dele estava atrás do maior valor gravado, ou uma instância da API recusa tokens ou `occurredAt` que as outras aceitam.

**Diagnóstico.** Veja `increase(ledger_recorded_at_corrections_total[15m])` e o evento 1005, compare `SELECT clock_timestamp();` no banco com a hora UTC dos servidores das instâncias e confira se `ledger_auth_failures_total` com `reason="expired"` se concentra numa instância.

**Ação.** Corrija a sincronização de tempo, com ajuste gradual e sem saltos, e retire a instância desviada de circulação enquanto isso. No banco as correções param quando o relógio alcança o maior `recorded_at` já usado. Nada é reescrito: os lançamentos ficam alguns microssegundos ou segundos à frente do tempo real, e quem consulta `asOf` dentro dessa janela deve ser informado.

**Confirmação e escalonamento.** O contador de correções parado e nenhuma recusa de token concentrada numa instância. Time de plataforma, para a sincronização de tempo do servidor de banco.
