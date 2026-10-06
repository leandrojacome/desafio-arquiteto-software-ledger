# Catálogo de métricas

O ledger exporta métricas próprias, todas definidas na classe `LedgerMeters`, sobre o medidor chamado `Ledger`. Cada uma aparece abaixo com o nome que sai no Prometheus, o tipo, a unidade, os rótulos e o que ela mede, e a página diz quais séries só existem em certas condições. Os sinais de saúde, os logs e os traces estão em [Saúde e observabilidade](saude-e-observabilidade.md), e as expressões de alerta que usam estas métricas estão em [Indicadores, objetivos e alertas](slos-e-alertas.md).

O `InstrumentCatalogTests` confere esta página contra o medidor: toda linha de tabela cuja primeira célula é um nome em minúsculas entre crases precisa ter instrumento, e todo instrumento precisa ter linha. Por isso as outras tabelas não começam por um nome assim, e as métricas de biblioteca (HTTP, Npgsql e runtime) trazem o nome na segunda coluna.

## Como os nomes se convertem

No código os nomes usam ponto (`ledger.entries.recorded`). Na saída para o Prometheus o ponto vira sublinhado, o contador ganha o sufixo `_total` e a unidade `s` vira o sufixo `_seconds` (`ledger.entry.duration` vira `ledger_entry_duration_seconds`). Unidades entre chaves, como `{entry}`, descrevem o que se conta e não aparecem no nome. O histograma gera as séries `_bucket`, `_sum` e `_count`. As tabelas abaixo já trazem o nome convertido.

Há três tipos. O contador só cresce e se consulta por taxa (`rate`) ou por aumento (`increase`). O histograma registra uma distribuição de durações, com os limites explícitos descritos mais adiante. O gauge é um valor lido na hora da coleta: aqui todos são observáveis, lidos de um retrato que o processo mantém em memória.

## Negócio e API

| Métrica | Tipo | Unidade | Rótulos | O que mede |
|---|---|---|---|---|
| `ledger_entries_recorded_total` | contador | `{entry}` | `type`, `client` | Lançamentos confirmados no banco, inclusive estornos (`type="reversal"`). Uma repetição idempotente não conta |
| `ledger_entries_rejected_total` | contador | `{entry}` | `reason`, `client` | Pedidos recusados pelas regras de negócio, com o motivo do conjunto fechado |
| `ledger_entry_duration_seconds` | histograma | `s` | `type`, `outcome` | Duração do caso de uso de lançamento e de estorno, da validação até o commit |
| `ledger_balance_query_duration_seconds` | histograma | `s` | `mode` | Duração do caso de uso de consulta de saldo, atual ou em um instante |
| `idempotency_replays_total` | contador | `{replay}` | `client` | Requisições respondidas a partir do resultado guardado (mesma chave, mesmo corpo) |
| `idempotency_conflicts_total` | contador | `{conflict}` | `client` | Mesma chave com corpo diferente, devolvida como `422` |
| `ledger_db_command_duration_seconds` | histograma | `s` | `operation` | Duração dos comandos SQL de leitura do caminho quente: `select_balance`, `select_balance_as_of` e `select_entries`. A escrita não emite este instrumento |
| `ledger_db_retries_total` | contador | `{retry}` | `reason` | Retentativas da transação de escrita antes do commit |
| `ledger_recorded_at_corrections_total` | contador | `{correction}` | `client` | Escritas em que o `GREATEST` empurrou o `recorded_at` à frente do relógio do banco. Qualquer valor acima de zero pede olhar o relógio do servidor |

Dos sete valores de `operation`, quatro (`insert_entry`, `update_balance`, `insert_outbox` e `insert_idempotency_key`) pertencem à escrita e nunca são emitidos, porque o código da escrita não cronometra os comandos. Para ver onde o tempo da escrita vai, use os spans de comando do Npgsql no trace e as estatísticas do próprio banco, como descrito em [Procedimentos de operação](runbooks.md). A cronometragem da escrita é um item de [Evolução futura](../11-evolucao/evolucao-futura.md).

## Outbox e Worker

Estas métricas vêm só do Worker. A API não as emite.

| Métrica | Tipo | Unidade | Rótulos | O que mede |
|---|---|---|---|---|
| `outbox_pending_messages` | gauge | `{message}` | nenhum | Mensagens ainda não publicadas, com teto de `Outbox:PendingCap` (1.000.000) na consulta |
| `outbox_oldest_pending_age_seconds` | gauge | `s` | nenhum | Idade da mensagem pendente mais antiga, ou zero sem pendência. É o melhor sinal de que a publicação parou |
| `outbox_published_total` | contador | `{message}` | nenhum | Mensagens publicadas e confirmadas pelo broker |
| `outbox_publish_failures_total` | contador | `{failure}` | `reason` | Tentativas de publicação que falharam, com o motivo. `unroutable` conta a mensagem que o broker devolveu porque nenhuma fila a recebeu |
| `outbox_publish_duration_seconds` | histograma | `s` | nenhum | Tempo da publicação até a confirmação do broker, só das publicações confirmadas |
| `outbox_failed_messages` | gauge | `{message}` | nenhum | Mensagens com `Outbox:FailedAttempts` tentativas ou mais (5) entre as `Outbox:FailedHeadWindow` pendentes mais antigas (1.000), que esperam intervenção humana |
| `outbox_pruned_total` | contador | `{message}` | nenhum | Mensagens já publicadas que a poda removeu |
| `broker_circuit_breaker_state` | gauge | sem unidade | nenhum | Estado do circuito do broker: 0 fechado, 1 meio aberto, 2 aberto |
| `broker_connected` | gauge | sem unidade | nenhum | 1 com a conexão e a topologia prontas, 0 sem conexão ou enquanto a topologia espera ser declarada de novo depois de uma devolução |
| `worker_last_cycle_timestamp_seconds` | gauge | `s` | `loop` | Instante, em segundos desde 1970, do último ciclo concluído de cada laço, com ou sem erro. Serve de batimento |
| `ledger_worker_loop_failures_total` | contador | `{failure}` | `loop` | Ciclos de um laço que terminaram em exceção. O laço segue vivo e tenta de novo, então só este contador e o evento 3008 mostram que ele falha sem parar |
| `ledger_worker_loop_last_success_timestamp_seconds` | gauge | `s` | `loop` | Instante do último ciclo que terminou sem exceção. Uma poda ou uma medição que falha sempre deixa de avançar aqui |

## Integridade, segurança e chaves

| Métrica | Tipo | Unidade | Rótulos | O que mede |
|---|---|---|---|---|
| `ledger_integrity_check_runs_total` | contador | `{run}` | `mode`, `result` | Execuções da conferência de integridade |
| `ledger_integrity_violations_total` | contador | `{violation}` | `kind` | Divergências encontradas: `balance_mismatch` para o topo da conta e `chain_broken` para a cadeia de lançamentos |
| `ledger_integrity_last_success_timestamp_seconds` | gauge | `s` | `mode` | Instante da última execução sem divergência |
| `ledger_integrity_check_duration_seconds` | histograma | `s` | `mode` | Duração de uma execução da conferência |
| `ledger_auth_failures_total` | contador | `{failure}` | `reason` | Recusas de autenticação e de escopo. O rótulo `expired` cobre token vencido e token ainda não válido |
| `ledger_rate_limit_rejections_total` | contador | `{rejection}` | `policy` | Requisições barradas por limite de taxa ou de concorrência |
| `ledger_accounts_created_total` | contador | `{account}` | nenhum | Contas criadas |
| `ledger_pii_decrypt_total` | contador | `{account}` | `purpose` | Leituras do documento em claro, contadas por conta |
| `ledger_key_reloads_total` | contador | `{reload}` | `result` | Recargas do conjunto de chaves |
| `ledger_audit_recorded_total` | contador | `{event}` | `event_type`, `outcome` | Eventos gravados na trilha de auditoria. As execuções da conferência de integridade são gravadas na trilha e não entram aqui |
| `ledger_audit_skipped_total` | contador | `{event}` | `reason` | Negações de escrita que a trilha não registrou |
| `ledger_rewrap_accounts_total` | contador | `{account}` | `result` | Contas processadas pela recifragem do documento |
| `ledger_pii_accounts_below_active_key` | gauge | `{account}` | nenhum | Contas cujo documento está cifrado com uma versão de chave anterior à ativa, lido ao fim de cada passada da recifragem. Só chega a zero quando a rotação termina, e é o número que autoriza retirar a chave antiga |

## Valores dos rótulos

Todo rótulo, exceto `client`, nasce de um enum fechado. Um texto que não pertença ao conjunto não vira rótulo: a medição é descartada em vez de inventar um valor. O `LabelTableTests` fixa os conjuntos, e o `MetricCardinalityTests` percorre os rótulos emitidos numa execução mista e recusa os de alta cardinalidade.

| Rótulo | Onde aparece | Valores |
|---|---|---|
| Tipo do lançamento (`type`) | Lançamentos e duração do caso de uso | `credit`, `debit`, `reversal` |
| Motivo da recusa (`reason`) | `ledger_entries_rejected_total` | `insufficient_funds`, `currency_mismatch`, `account_not_found`, `idempotency_conflict`, `entry_already_reversed`, `entry_not_reversible`, `entry_not_found`, `validation` |
| Resultado do caso de uso (`outcome`) | `ledger_entry_duration_seconds` | `recorded`, `replayed`, `rejected`, `failed` |
| Modo do saldo (`mode`) | `ledger_balance_query_duration_seconds` | `current`, `as_of` |
| Operação SQL (`operation`) | `ledger_db_command_duration_seconds` | Emitidos: `select_balance`, `select_balance_as_of`, `select_entries`. Definidos e sem emissão: `insert_entry`, `update_balance`, `insert_outbox`, `insert_idempotency_key` |
| Motivo da retentativa (`reason`) | `ledger_db_retries_total` | `deadlock`, `serialization_failure`, `transient_connection` |
| Motivo da falha de publicação (`reason`) | `outbox_publish_failures_total` | `broker_unavailable`, `nack`, `timeout`, `serialization`, `unroutable` |
| Laço do Worker (`loop`) | `worker_last_cycle_timestamp_seconds`, `ledger_worker_loop_failures_total` e `ledger_worker_loop_last_success_timestamp_seconds` | `outbox`, `integrity-recent`, `integrity-full`, `measure`, `prune`, `idempotency-prune`, `rewrap` |
| Modo da conferência (`mode`) | Métricas de integridade | `incremental`, `full` |
| Resultado da conferência (`result`) | `ledger_integrity_check_runs_total` | `ok`, `violation`, `error` |
| Tipo do achado (`kind`) | `ledger_integrity_violations_total` | `balance_mismatch`, `chain_broken` |
| Motivo da recusa de autenticação (`reason`) | `ledger_auth_failures_total` | `missing_token`, `invalid_token`, `expired`, `insufficient_scope`, `missing_client_id`, `invalid_client_id` |
| Política de limite (`policy`) | `ledger_rate_limit_rejections_total` | `write-per-client`, `read-per-client`, `write-per-account`, `write-concurrency`, `balance-concurrency`, `statement-concurrency` |
| Finalidade da leitura do documento (`purpose`) | `ledger_pii_decrypt_total` | `rewrap`, `holder_lookup`, `investigation` |
| Resultado da recarga de chaves (`result`) | `ledger_key_reloads_total` | `ok`, `failed` |
| Tipo de evento auditado (`event_type`) | `ledger_audit_recorded_total` | `account.created`, `authorization.denied_write`, `pii.decrypted`, `pii.rewrapped`, `keys.version_activated` |
| Resultado do evento auditado (`outcome`) | `ledger_audit_recorded_total` | `SUCCESS`, `DENIED` |
| Motivo da negação não registrada (`reason`) | `ledger_audit_skipped_total` | `rate_capped`, `write_failed` |
| Resultado da recifragem (`result`) | `ledger_rewrap_accounts_total` | `rewrapped`, `failed` |

### O rótulo `client`

`client` é o único rótulo que não vem de um conjunto fixo, porque os sistemas chamadores são poucos e conhecidos, na casa das dezenas. Ele existe só nas métricas de lançamento, de repetição, de conflito e de correção de `recorded_at`. O valor vem da claim `client_id` do token e só é aceito com até 64 caracteres, só letras, dígitos e os símbolos `.`, `_`, `:`, `@` e `-`, e se não parecer um token ou outro dado sensível. Um valor que falhe nisso, ou a ausência de token, vira `unknown`. Depois de 128 clientes distintos no mesmo processo, os novos viram `other`. O `ClientLabelsTests` fixa as três regras.

### O que nunca é rótulo

Nunca entram como rótulo, atributo de span ou propriedade de métrica o `account_id`, o `entry_id`, o `correlation_id`, a `Idempotency-Key`, o documento do titular e o `message_id`. Eles ficam nos logs e nos traces, onde a cardinalidade não derruba o armazenamento. A rota é sempre o modelo (`/v1/accounts/{accountId}/entries`), nunca o caminho real.

## Limites dos histogramas

Os histogramas usam limites explícitos para que as metas de latência possam ser calculadas: sem um limite em 150 ms, o objetivo de latência da escrita não sairia exato do histograma HTTP. Os limites são configurados por instrumento em `HistogramBoundaries`, em segundos.

| Histograma | Limites, em segundos |
|---|---|
| Duração HTTP (`http_server_request_duration_seconds`) | `0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.25, 0.5, 1, 2.5, 5` |
| Casos de uso (`ledger_entry_duration_seconds` e `ledger_balance_query_duration_seconds`) | `0.002, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.25, 0.5, 1, 2.5` |
| Comandos SQL (`ledger_db_command_duration_seconds`) | `0.001, 0.002, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1` |
| Publicação (`outbox_publish_duration_seconds`) | `0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5` |
| Conferência de integridade (`ledger_integrity_check_duration_seconds`) | `0.1, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600, 900, 1800, 3600` |

O último limite da publicação (5 s) coincide com o prazo de confirmação do lote, `Outbox:ConfirmTimeoutSeconds`.

## Quando uma série existe

Algumas séries só existem em certas condições, e um alerta que as consulta precisa saber disso.

As métricas do outbox e do broker (`outbox_*`, `broker_*` e `worker_last_cycle_timestamp_seconds`) só existem no processo do Worker. Os três gauges de acúmulo (`outbox_pending_messages`, `outbox_oldest_pending_age_seconds` e `outbox_failed_messages`) leem o retrato que o laço de medição atualiza a cada `Outbox:MeasureIntervalSeconds` (10 s). Quando o retrato tem mais de três intervalos (30 s), as três séries deixam de ser emitidas, para que um valor velho não pareça saudável. Se o Worker perder o banco, o valor não fica congelado: a série some. Um alerta do tipo "idade maior que X" não dispara quando a série some, e é por isso que existem os alertas de batimento e de ausência de série.

`ledger_integrity_last_success_timestamp_seconds` só passa a existir, para cada modo, depois da primeira execução sem divergência no processo, e `ledger_worker_loop_last_success_timestamp_seconds` depois do primeiro ciclo bem-sucedido de cada laço. `ledger_pii_accounts_below_active_key` só existe depois da primeira passada da recifragem. As três ficam na memória do processo, então um Worker reiniciado as reconstrói a partir da primeira execução.

## Métricas de biblioteca

As bibliotecas de instrumentação acrescentam séries que o ledger não define e que o teste do catálogo não confere. Os nomes abaixo vêm dos instrumentos das bibliotecas na versão em uso (Npgsql 10) e da conversão padrão para o Prometheus. O nome final depende do exportador ou do coletor que receber os dados por OTLP, e o repositório não fixa essa configuração. Painéis e alertas devem conferir os nomes no ambiente onde forem criados e de novo a cada atualização dos pacotes.

| Assunto | Nome no Prometheus | Rótulos principais | Origem |
|---|---|---|---|
| Duração das requisições HTTP, em `_bucket`, `_sum` e `_count` | `http_server_request_duration_seconds` | `http_route`, `http_request_method`, `http_response_status_code` | ASP.NET Core |
| Requisições em curso | `http_server_active_requests` | | ASP.NET Core |
| Conexões do pool por estado | `db_client_connection_count` | `db_client_connection_state` (`idle` ou `used`), `db_client_connection_pool_name` | Npgsql |
| Máximo de conexões do pool | `db_client_connection_max` | `db_client_connection_pool_name` | Npgsql |
| Pedidos esperando uma conexão do pool | `db_client_connection_npgsql_pending_requests` | `db_client_connection_pool_name` | Npgsql |
| Esperas por conexão que estouraram o prazo | `db_client_connection_npgsql_timeouts_total` | `db_client_connection_pool_name` | Npgsql |
| Tempo para criar uma conexão nova | `db_client_connection_npgsql_create_time_seconds` | `db_client_connection_pool_name` | Npgsql |
| Duração dos comandos do banco | `db_client_operation_duration_seconds` | `db_system_name` e outros atributos do Npgsql | Npgsql |
| Comandos que falharam | `db_client_operation_failed_total` | idem | Npgsql |
| Comandos em execução | `db_client_operation_npgsql_executing` | idem | Npgsql |
| Coleta de lixo, fila do pool de threads e demais métricas de runtime | Definidas pela biblioteca de runtime | | `OpenTelemetry.Instrumentation.Runtime` |

## Testes que conferem o catálogo

| Regra | Teste |
|---|---|
| Todo instrumento do catálogo existe com a unidade e o tipo declarados, o medidor não tem outro além deles, e a página e o medidor batem sob o nome do Prometheus | `InstrumentCatalogTests` |
| Rótulos de conjunto fechado e sem alta cardinalidade | `LabelTableTests` e `MetricCardinalityTests` |
| `client` limitado a 64 caracteres seguros e a 128 valores distintos | `ClientLabelsTests` |
| Contadores sobem na quantidade certa nos casos de uso reais | `EntryTelemetryTests`, `EntryHandlerTelemetryTests` e `ReadTelemetryTests` |
| Métricas do outbox e do broker refletem a tabela e o estado do circuito | `OutboxTelemetryTests` e `BrokerAndLagHealthCheckTests` |
| Métricas de integridade e de segurança | `IntegrityTelemetryTests` e `SecurityTelemetryTests` |
| Limites explícitos dos histogramas | `TelemetryProviderTests` |
| Nenhum atributo de span carrega dado sensível | `SpanAttributesPrivacyTests` |
