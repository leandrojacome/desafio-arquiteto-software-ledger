# Indicadores, objetivos e alertas

A página define cinco objetivos de nível de serviço derivados das metas de [Requisitos não funcionais](../02-contexto-e-requisitos/requisitos-nao-funcionais.md), a política do orçamento de erro, os alertas que pedem ação e a origem do limiar de cada um. Os objetivos de disponibilidade e de latência dependem de medição em escala que ainda não existe ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)), e as regras de alerta e os painéis não estão provisionados no repositório, porque a pilha de monitoração é externa. O repositório garante que as métricas, os logs e as sondas que as regras consomem existem e se comportam como descrito em [Catálogo de métricas](catalogo-de-metricas.md) e [Saúde e observabilidade](saude-e-observabilidade.md).

## Objetivos e indicadores

A latência é medida no servidor, da entrada da requisição até a resposta, sem a rede entre o chamador e a API.

| Objetivo | Indicador | Meta | Janela |
|---|---|---|---|
| Disponibilidade | Respostas que não são `5xx` sobre o total das rotas `/v1/*`, em `http_server_request_duration_seconds_count` | 99,95% (NFR-05) | 30 dias |
| Latência de escrita | Fração das respostas `POST` de lançamento e de estorno até 150 ms, no bucket `le="0.15"` do histograma HTTP | 99% (NFR-03) | 30 dias |
| Latência de saldo | Fração das respostas `GET .../balance` até 50 ms, no bucket `le="0.05"` | 99% (NFR-04) | 30 dias |
| Frescor do outbox | Fração do tempo em que `outbox_oldest_pending_age_seconds` está em até 30 s, amostrado a cada 10 s | 99,9% (NFR-13) | 30 dias |
| Integridade | Divergências detectadas pela conferência, em `ledger_integrity_violations_total` | Zero (BR-17) | Contínuo |

Nenhuma operação mediu se 99,95% em 30 dias é alcançável com a topologia de alta disponibilidade desenhada, e com uma instância de banco só não é. O frescor do outbox é o indicador possível para a meta de p99 de 5 s entre o commit e a publicação (NFR-13), porque o ledger não mede o atraso de cada evento, só a idade da pendente mais antiga. O atraso por evento só se verifica no teste fim a fim (`EventLatencyE2ETests`, em escala local). O limiar de 30 s dá seis vezes de margem sobre a meta e nunca foi calibrado com tráfego real.

Alguns detalhes mudam o resultado. Respostas `4xx` contam como boas para a disponibilidade, porque um `422` por saldo insuficiente é o sistema funcionando, e o `429` também conta como bom, mas tem alerta próprio. O `503` do limite de concorrência conta como ruim, porque quem recusou foi o próprio ledger, e o `499` de um chamador que desistiu não é `5xx`. A latência de escrita considera a rota inteira, com autenticação e validação, porque o chamador sente a rota.

Um indicador por métrica da aplicação não enxerga a recusa que um balanceador faz sozinho quando todas as instâncias saem de circulação, então o ideal é medir a disponibilidade também no balanceador. A integridade não tem orçamento: uma única divergência é incidente (BR-17), detectada pelas duas execuções da conferência ([Fluxo: conferência de integridade](../06-fluxos/conferencia-de-integridade.md)).

## Orçamento de erro

Os 99,95% em 30 dias dão 21,6 minutos de orçamento (43.200 minutos vezes 0,0005), o que é pouco: um failover que leve os 15 minutos do RTO consome 69% do mês de uma vez, e por isso o alvo operacional do failover é bem menor que o RTO. A divisão sugerida é de uns dez minutos para o banco (failover e manutenção), cinco para implantação e rede e seis de reserva. Com mais de 50% do orçamento gasto na janela, mudanças que não sejam correção de confiabilidade esperam, e com 100% só entra trabalho de confiabilidade até o orçamento se recompor. Os objetivos de latência e de frescor seguem a mesma regra, em escala menor.

Para a disponibilidade o alerta é pela taxa de consumo do orçamento, porque um limiar fixo de `5xx` é ruidoso em minutos calmos e cego em dias longos. Cada alerta combina uma janela longa, que mostra que o consumo é relevante, e uma curta, que faz o alerta apagar rápido quando o problema acaba. A fração de `5xx` é o fator vezes 0,05%, o ritmo que esgotaria o orçamento em exatamente 30 dias.

| Janela longa | Janela curta | Fator | Fração de `5xx` | Orçamento consumido | Severidade | Alerta |
|---|---|---|---|---|---|---|
| 1 h | 5 min | 14,4 | 0,72% | 2% do mês, em 1 hora | Página | `LedgerAvailabilityFastBurn` |
| 6 h | 30 min | 6 | 0,30% | 5% do mês, em 6 horas | Página | `LedgerAvailabilitySlowBurn` |
| 3 d | 6 h | 1 | 0,05% | 10% do mês, em 3 dias | Ticket | `LedgerAvailabilityBudgetDrift` |

## Regras de referência

Os alertas de disponibilidade comparam a razão de `5xx` com o limiar da tabela nas duas janelas. A razão é a taxa de `http_server_request_duration_seconds_count` com `http_response_status_code=~"5.."` sobre a de todas as respostas das rotas `/v1/.*`, calculada como regra de gravação por janela (5 minutos, 30 minutos, 1 hora, 6 horas e 3 dias). A regra que exige cuidado é a da integridade, em formato do Prometheus. Ela não rodou contra tráfego do ledger e deve ser validada com `promtool check rules` e `promtool test rules` no ambiente que a hospedar.

```yaml
groups:
  - name: ledger-alerts
    rules:
      - alert: LedgerIntegrityViolation
        expr: |
          increase(ledger_integrity_violations_total[10m]) > 0
          or
          (ledger_integrity_violations_total > 0 unless ledger_integrity_violations_total offset 10m)
        labels:
          severity: page
```

O contador do OpenTelemetry só passa a existir na primeira ocorrência, e `increase` não enxerga o salto de uma série ausente para o valor 1. Por isso o alerta de integridade combina `increase` com a condição de série recém-criada, sem o que a primeira divergência de cada processo passaria em silêncio, e o mesmo padrão vale para qualquer alerta do tipo "qualquer ocorrência". Ele se apaga sozinho depois de 10 minutos, embora o achado continue em `audit_log`: a verdade sobre o que está em aberto está na trilha e não no alerta ([Procedimentos de operação](runbooks.md)).

As séries do outbox e do broker somem quando o Worker perde o banco ou quando nenhuma instância está viva, e um alerta de "idade maior que X" não dispara quando a série some. É o buraco que `WorkerStalled` (Worker vivo e parado) e `WorkerMetricsAbsent` (Worker ausente) cobrem. As consultas dos alertas de latência são:

```text
histogram_quantile(0.99, sum by (le) (rate(http_server_request_duration_seconds_bucket{http_route=~"/v1/accounts/.+/entries.*",http_request_method="POST"}[5m])))
histogram_quantile(0.99, sum by (le) (rate(http_server_request_duration_seconds_bucket{http_route="/v1/accounts/{accountId}/balance",http_request_method="GET"}[5m])))
```

## Catálogo de alertas

Cada alerta pede uma ação, e a severidade diz se ela é para agora (página, que acorda alguém) ou para o próximo dia útil (ticket). A coluna Origem diz de onde vem o limiar: uma meta, um valor de configuração ou, em "valor inicial", uma escolha sem medição, a calibrar com o tráfego real. A coluna Procedimento nomeia a seção de [Procedimentos de operação](runbooks.md) que trata o alerta.

### Disponibilidade e latência

| Alerta | Severidade | Condição | Duração | Origem | Procedimento |
|---|---|---|---|---|---|
| `LedgerAvailabilityFastBurn` | Página | Fração de `5xx` acima de 0,72% em 1 h e em 5 min | Imediato | 14,4 vezes o ritmo do orçamento | Erros 5xx |
| `LedgerAvailabilitySlowBurn` | Página | Fração de `5xx` acima de 0,3% em 6 h e em 30 min | Imediato | 6 vezes o ritmo do orçamento | Erros 5xx |
| `LedgerAvailabilityBudgetDrift` | Ticket | Fração de `5xx` acima de 0,05% em 3 dias e em 6 h | Imediato | O ritmo do orçamento | Erros 5xx |
| `ApiReadinessFailing` | Página | `sum(rate(http_server_request_duration_seconds_count{http_route="/health/ready",http_response_status_code="503"}[2m])) > 0` | 5 min | Valor inicial: readiness em falha por 5 minutos tira instâncias de circulação | Banco indisponível ou em failover |
| `LedgerWriteLatencyHigh` | Ticket | p99 do `POST` de lançamento acima de 150 ms | 15 min | NFR-03 | Latência de escrita alta |
| `LedgerWriteLatencyCritical` | Página | p99 do `POST` de lançamento acima de 500 ms | 5 min | Valor inicial: mais de três vezes a meta | Latência de escrita alta |
| `LedgerBalanceLatencyHigh` | Ticket | p99 do `GET .../balance` acima de 50 ms | 15 min | NFR-04 | Latência de escrita alta |
| `DbRetryRateHigh` | Ticket | `sum(rate(ledger_db_retries_total[15m])) / sum(rate(ledger_entries_recorded_total[15m]))` acima de 0,01 | 15 min | Limiar do [documento de arquitetura 0011](../03-principios-e-decisoes/documento-arquitetura/0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md): acima de 1% é contenção, não falha transitória | Latência de escrita alta |
| `DbPoolSaturated` | Ticket | `db_client_connection_npgsql_pending_requests` acima de 0 | 2 min | A espera por conexão do pool é de 1 s, então fila sustentada é saturação | Saturação e limites |
| `RateLimitSustained` | Ticket | `sum(rate(ledger_rate_limit_rejections_total[5m]))` acima de 10 por segundo | 10 min | Valor inicial | Saturação e limites |

### Outbox, broker e Worker

| Alerta | Severidade | Condição | Duração | Origem | Procedimento |
|---|---|---|---|---|---|
| `OutboxOldestPendingAgeHigh` | Ticket | `outbox_oldest_pending_age_seconds` acima de 60 | 2 min | Alertar antes de o consumidor perceber (60 s de aviso, 5 min de chamado) e limiar `Degraded` do Worker | Outbox sem drenar |
| `OutboxOldestPendingAgeCritical` | Página | `outbox_oldest_pending_age_seconds` acima de 300 | 5 min | A mesma meta | Outbox sem drenar |
| `OutboxBacklogLarge` | Ticket | `outbox_pending_messages` acima de 100.000 | 5 min | Valor inicial: uns 8 minutos de escrita na média premissada de 200 por segundo | Outbox sem drenar |
| `OutboxFailedMessages` | Ticket | `outbox_failed_messages` acima de 0 | 10 min | `Outbox:FailedAttempts` (5) | Outbox sem drenar |
| `BrokerDisconnected` | Ticket | `broker_connected` igual a 0 | 5 min | Sem conexão utilizável o publicador não reivindica nada, e o circuito pode continuar fechado | Outbox sem drenar |
| `BrokerCircuitOpen` | Ticket | `broker_circuit_breaker_state` igual a 2 | 2 min | `Resilience:BrokerCircuitBreaker:BreakSeconds` (30 s) | Outbox sem drenar |
| `OutboxUnroutable` | Ticket | `increase(outbox_publish_failures_total{reason="unroutable"}[5m]) > 0` | 5 min | Nenhuma fila está ligada à troca | Outbox sem drenar |
| `WorkerStalled` | Página | Segundos desde `worker_last_cycle_timestamp_seconds{loop="outbox"}` acima de 120 | 1 min | `Resilience:Health:OutboxHeartbeatSeconds` | Worker parado ou laço falhando |
| `WorkerMetricsAbsent` | Página | `absent(worker_last_cycle_timestamp_seconds{loop="outbox"})` | 5 min | Nenhuma instância do Worker está exportando | Worker parado ou laço falhando |
| `WorkerLoopFailing` | Ticket | `sum by (loop) (increase(ledger_worker_loop_failures_total[1h]))` acima de 5, ou `time()` menos `ledger_worker_loop_last_success_timestamp_seconds` acima de três vezes o intervalo do laço | 10 min | Valor inicial | Worker parado ou laço falhando |

`WorkerStalled` existe separado da idade do outbox porque a idade só sobe quando há mensagem pendente, e um Worker parado numa madrugada sem tráfego só apareceria depois. Os intervalos dos laços, para a segunda condição de `WorkerLoopFailing`, são `Outbox:MeasureIntervalSeconds` (10 s), `Outbox:PruneIntervalMinutes` e `Idempotency:PruneIntervalMinutes` (10 minutos), `Security:Pii:Rewrap:IdleSeconds` (60 s) e `Integrity:RecentIntervalMinutes` (5 minutos, que vale também para o laço completo).

### Integridade, chaves e segurança

| Alerta | Severidade | Condição | Duração | Origem | Procedimento |
|---|---|---|---|---|---|
| `LedgerIntegrityViolation` | Página | Divergência nova em `ledger_integrity_violations_total`, pela regra acima | Imediato | BR-17: toda divergência é incidente | Divergência de integridade |
| `LedgerIntegrityCheckStale` | Ticket | `time()` menos `ledger_integrity_last_success_timestamp_seconds` acima de 900 s no modo `incremental`, ou de 93.600 s no `full` | 5 min | Três vezes `Integrity:RecentIntervalMinutes`, e `Integrity:FullIntervalHours` mais 2 horas | Divergência de integridade |
| `RecordedAtCorrectionsDetected` | Ticket | `increase(ledger_recorded_at_corrections_total[15m]) > 0`, mais a condição de série recém-criada | Imediato | O banco corrigiu um `recorded_at` e o relógio do servidor merece conferência | Relógio com desvio |
| `KeyReloadFailing` | Ticket | `increase(ledger_key_reloads_total{result="failed"}[30m])` igual ou acima de 3 | Imediato | A recarga é a cada `Security:Pii:ReloadMinutes` (10 minutos) | Chaves de dados pessoais |
| `PiiRewrapPending` | Ticket | `ledger_pii_accounts_below_active_key` acima de 0 | 24 h | Valor inicial: a rotação deve terminar em um dia | Chaves de dados pessoais |
| `AuthFailureSpike` | Ticket de segurança | `sum(rate(ledger_auth_failures_total{reason!="insufficient_scope"}[5m]))` acima de 20 por segundo | 5 min | Valor inicial | Autenticação e uso indevido |
| `IdempotencyConflictSpike` | Ticket | `sum(rate(idempotency_conflicts_total[10m]))` acima de 1 por segundo | 10 min | Valor inicial | Autenticação e uso indevido |
| `ClientDebitVolumeAnomaly` | Ticket de segurança | Taxa de débitos de um `client` acima de 3 vezes a da mesma hora na semana anterior, com base acima de 1 por segundo | 10 min | Valor inicial | Autenticação e uso indevido |
| `ClientInsufficientFundsProbe` | Ticket de segurança | `sum by (client) (rate(ledger_entries_rejected_total{reason="insufficient_funds"}[5m]))` acima de 5 por segundo | 10 min | Valor inicial | Autenticação e uso indevido |
| `ClientReversalVolumeAnomaly` | Ticket de segurança | Taxa de `ledger_entries_recorded_total{type="reversal"}` de um `client` acima de 3 vezes a da mesma hora na semana anterior, com base acima de 0,5 por segundo | 10 min | Valor inicial | Autenticação e uso indevido |
| `BalanceNotFoundScan` | Ticket de segurança | Respostas `404` em `GET .../balance` acima de 5 por segundo | 5 min | Valor inicial | Autenticação e uso indevido |

`LedgerIntegrityCheckStale` cobre a conferência que parou. Depois de uma divergência, o modo incremental deixa de registrar sucesso nas execuções que a reencontram, e por isso o alerta de ausência de sucesso também dispara, o que serve de segunda via.

O `ClientDebitVolumeAnomaly` é a principal proteção contra o chamador comprometido: se o sistema de cartões começa a debitar três vezes mais que o normal, ninguém no ledger sabe que é fraude, mas alguém precisa saber que mudou. O `ClientInsufficientFundsProbe` e o `ClientReversalVolumeAnomaly` cobrem o outro uso indevido do escopo de escrita, descobrir o saldo de uma conta por tentativas de débito e estornos dos que passaram. O ledger não separa uma busca de saldo de um débito legítimo recusado, e o que faz é tornar o padrão visível. A readiness `keys` em `Degraded` não aparece como métrica, e por isso `KeyReloadFailing` é o alerta de métrica da fonte de chaves, complementado por uma sonda sintética sobre `/health/ready`, quando houver.
