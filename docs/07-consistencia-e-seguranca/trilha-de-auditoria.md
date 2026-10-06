# Trilha de auditoria

A trilha é a tabela `audit_log`, só de inserção, e guarda o que o livro de lançamentos não registra: quem criou uma conta, quem tentou escrever sem permissão, quando o documento do titular foi decifrado, quando as chaves mudaram e o que a conferência de integridade encontrou. Um lançamento aceito já é o seu próprio registro e não gera linha aqui. A página define o catálogo de tipos, as chaves permitidas em `details`, o que fica de fora e os controles de imutabilidade e de acesso. A tabela está em [Modelo de dados](../05-contratos/modelo-de-dados.md), e a decisão do catálogo fechado, no [documento de arquitetura 0028](../03-principios-e-decisoes/documento-arquitetura/0028-trilha-de-auditoria-com-catalogo-fechado.md).

## Três fontes de rastro

O ledger deixa rastro em três lugares, e cada um responde a uma pergunta diferente. A linha de `ledger_entries` carrega, em todo lançamento aceito, `client_id`, `correlation_id`, `recorded_at` (relógio do banco) e `balance_after`, gravados na mesma transação. Auditá-lo com uma segunda escrita duplicaria o dado e custaria uma inserção a mais por lançamento a 2.000 por segundo. A tabela `audit_log` recebe as ações administrativas e de segurança do catálogo abaixo, com `id`, `recorded_at`, `event_type`, `client_id`, `account_id` quando existir, `correlation_id`, `outcome` e `details` em `jsonb`, sem dado pessoal. E o log estruturado recebe as consultas de saldo e de extrato como `BalanceQueried` (2001) e `StatementQueried` (2002), na categoria `Ledger.Audit`, em `Information`, com `client_id`, conta, modo e `asOf`, e nenhum valor monetário. Esse volume não vai para o banco. Se o destino de logs o suporta na escala das premissas, ou se ele precisa de agregação, ainda não foi medido ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)).

## O catálogo

O catálogo é fechado no código: `AuditEventTypes` declara os sete tipos e `AuditEvents` é a única forma de construir um `AuditEvent`. Não há caminho para gravar um tipo fora da lista nem um `details` com texto livre. O `outcome` é restrito por `ck_audit_log_outcome` a `SUCCESS`, `DENIED` e `FAILURE`.

| `event_type` | Quem grava | Quando | `outcome` | `client_id` | `account_id` |
|---|---|---|---|---|---|
| `account.created` | `CreateAccountHandler`, dentro da transação da criação | A conta e o saldo inicial foram inseridos | `SUCCESS` | O do token | A conta criada |
| `authorization.denied_write` | `DeniedWriteAuditor`, fora de qualquer transação | Token válido sem `ledger.write` numa rota de escrita, ou cliente fora da lista de provisionamento em `POST /v1/accounts` | `DENIED` | O do token | O da rota, se for um GUID válido, ou nulo |
| `pii.decrypted` | `PostgresAccountKeyRewrapper`, na transação do lote | Um lote da recifragem decifrou documentos em claro (só se ao menos uma conta foi atualizada) | `SUCCESS` | `ledger-worker` | Nulo |
| `pii.rewrapped` | `PostgresAccountKeyRewrapper`, na transação do lote | Um lote da recifragem foi concluído | `SUCCESS` | `ledger-worker` | Nulo |
| `keys.version_activated` | `RecordKeyActivationHandler`, na subida do Worker | A versão ativa das chaves ainda não estava registrada | `SUCCESS` | `ledger-worker` | Nulo |
| `integrity.violation_detected` | `PostgresIntegritySession`, durante a conferência | A conferência achou uma divergência | `FAILURE` | `ledger-worker` | A conta com a divergência |
| `integrity.run_completed` | `PostgresIntegritySession`, ao fim da execução | Uma execução terminou, inclusive a interrompida (parcial) | `SUCCESS`, ou `FAILURE` se houve divergência | `ledger-worker` | Nulo |

O `correlation_id` é o da requisição nos dois primeiros tipos. Nos demais, que nascem no Worker, é um identificador de execução (um GUID sem hifens), o mesmo em todas as linhas da mesma passada. Os fluxos de cada gravação estão em [Fluxo: criação de conta](../06-fluxos/criacao-de-conta.md), [Fluxo: rotação das chaves de dados pessoais](../06-fluxos/rotacao-de-chaves.md) e [Fluxo: conferência de integridade](../06-fluxos/conferencia-de-integridade.md).

### O conteúdo de `details`

Cada tipo tem uma lista fechada de chaves, com valores inteiros, textos de um conjunto fechado ou a rota pelo modelo, e o `AuditEvents` recusa uma rota que não seja um modelo, isto é, um caminho com identificador real. O PostgreSQL normaliza a ordem das chaves do `jsonb`, então a ordem de leitura difere da de escrita.

| `event_type` | Chaves de `details` | Exemplo |
|---|---|---|
| `account.created` | Objeto vazio | `{}` |
| `authorization.denied_write` | `route` (modelo da rota), `requiredScope` (`ledger.write`) e `reason` (`insufficient_scope` ou `not_provisioning_client`) | `{"route": "POST /v1/accounts/{accountId}/entries", "requiredScope": "ledger.write", "reason": "insufficient_scope"}` |
| `pii.decrypted` | `purpose` (`rewrap`) e `accounts` | `{"purpose": "rewrap", "accounts": 500}` |
| `pii.rewrapped` | `fromVersion` (a menor versão do lote), `toVersion`, `accounts` e `failed` | `{"fromVersion": 1, "toVersion": 2, "accounts": 500, "failed": 0}` |
| `keys.version_activated` | `version` | `{"version": 2}` |
| `integrity.violation_detected` | `runId`, `mode` (`RECENT` ou `FULL`), `check` (`HEAD_BALANCE`, `HEAD_VERSION`, `HEAD_LAST_ENTRY`, `HEAD_FLOOR`, `CHAIN_DRIFT`, `CHAIN_GAP`, `CHAIN_NON_MONOTONIC` ou `SUM_BALANCE`), `entryId` e `accountVersion` quando houver, `expected` e `found` | Em [Fluxo: conferência de integridade](../06-fluxos/conferencia-de-integridade.md) |
| `integrity.run_completed` | `mode`, `windowStart`, `windowEnd`, `accountsChecked`, `entriesChecked`, `violations` e `partial` (só se interrompida) | Idem |

Nenhum documento, blob cifrado, índice cego, token, chave, texto livre, corpo de requisição ou caminho real entra em `details`. Os valores `expected` e `found` de uma divergência são saldos e versões, dado financeiro que não identifica o titular, e é por isso que ler a trilha exige acesso restrito. O `AuditDetailsPrivacyTests` percorre todas as linhas de uma execução completa em busca de valores canário de documento, blob e índice.

## O que não entra

| Fato | Onde fica |
|---|---|
| Lançamento aceito | A própria linha de `ledger_entries` |
| Recusa de negócio (saldo insuficiente, chave reutilizada, moeda diferente) | Log estruturado e métrica, com o mesmo `correlation_id` |
| Falha de autenticação (401) | Log 6001 e `ledger_auth_failures_total`. Gravar no banco transformaria uma força bruta em carga de escrita |
| Token sem `client_id` ou com `client_id` inválido (403) | Log 6002 e métrica. Sem identidade válida não há `client_id` para gravar |
| Consulta de saldo ou de extrato | Eventos de log 2001 e 2002 |
| Negação de escrita além do teto por chamador | `ledger_audit_skipped_total` com `reason="rate_capped"` e o log 6003 (`Warning`, que cita só o chamador) |
| Negação de escrita cuja gravação falhou | `ledger_audit_skipped_total` com `reason="write_failed"` e o log 6004 (`Warning`, que cita só o tipo da exceção) |

O limite dessa escolha é que logs têm retenção menor que a do ledger. Se a regulação exigir guardar as tentativas negadas por dez anos, ou todas e não uma amostra, o caminho é um armazenamento de auditoria externo e imutável, não mais linhas no banco do ledger.

## A negação de escrita é melhor esforço

A gravação de `authorization.denied_write` nunca decide a resposta. O 403 sai com o mesmo conteúdo e no mesmo tempo, grave ou não a trilha, porque o `LedgerAuthorizationResultHandler` chama `DeniedWriteAuditor.Record` e segue sem esperar, e a gravação roda numa tarefa própria que captura qualquer exceção, com prazo de 5 segundos. O teto existe porque um token de leitura válido gera um 403 a cada tentativa de escrita, e a cota por chamador deixaria passar até 1.500 por segundo, que seriam 1.500 inserções por segundo no banco. Cada chamador tem um balde de capacidade 10 e reposição de 1 por segundo (`Security:Audit:DeniedWrite:Capacity` e `RefillPerSecond`, ambas entre 1 e 1.000, e valor fora da faixa impede a subida). Com o teto, o rastro de uma tentativa isolada fica completo e o de uma rajada fica amostrado.

O auditor esquece o balde de um chamador depois de uma hora sem uso e, ao encerrar o processo, espera por até 5 segundos as gravações em andamento. Um 403 de rota com identificador que não é GUID grava a linha sem `account_id`, e o 403 sem `client_id` não grava, porque a coluna é obrigatória e um valor não confiável não pode entrar na trilha.

## Imutabilidade e acesso

A trilha vale o que valer a garantia de que ninguém a editou, então tem os mesmos controles do ledger. `ledger_api` e `ledger_worker` só inserem, o Worker lê apenas `id`, `recorded_at`, `event_type`, `outcome` e `details`, as cinco colunas de que a conferência precisa, e nenhum dos dois tem `UPDATE`, `DELETE` ou `TRUNCATE`. Os gatilhos `tr_audit_log_forbid_update_delete` e `tr_audit_log_forbid_truncate` barram essas operações até para o dono do esquema, com `integrity_constraint_violation`. O `ledger_readonly` lê todas as colunas e não escreve. Nenhuma rotina remove linhas, e a guarda de dez anos é a premissa regulatória da BR-18. Quem tem papel de dono do esquema ou de superusuário ainda desliga os gatilhos, e o registro de comandos do banco em produção (`pgaudit`) é controle de implantação ([Segurança](seguranca.md)). Os índices `ix_audit_log_event_type_recorded_at` e `ix_audit_log_account_id_recorded_at` atendem as consultas por tipo e por conta.

Duas consultas de apoio a quem investiga, com o papel `ledger_readonly` (o sistema não as emite):

```text
SELECT recorded_at, client_id, details ->> 'route' AS route, details ->> 'reason' AS reason
FROM audit_log
WHERE event_type = 'authorization.denied_write'
  AND recorded_at >= now() - interval '24 hours'
ORDER BY recorded_at DESC;
```

```text
SELECT recorded_at, event_type, details
FROM audit_log
WHERE event_type IN ('pii.decrypted', 'pii.rewrapped', 'keys.version_activated')
ORDER BY recorded_at DESC;
```

## Testes

| Regra | Teste |
|---|---|
| Cada tipo sai com a forma exata dos `details`, e a rota que não é modelo é rejeitada | `AuditEventsTests` (Ledger.Application.Tests), `PostgresAuditTrailTests` |
| `account.created` grava na mesma transação da conta | `PostgresAuditTrailTests`, `CreateAccountTests` |
| A negação é gravada com a rota em modelo, e o 403 não muda se a gravação falha ou demora | `AuthorizationDeniedAuditTests`, `DeniedWriteAuditorTests` (Ledger.Infrastructure.Tests) |
| Teto por chamador, reposição e balde próprio de cada chamador | `DeniedWriteAuditIntegrationTests`, `DeniedWriteAuditorTests` |
| Pedido sem token e token sem `client_id` não deixam linha | `AuthorizationDeniedAuditTests` |
| A trilha não aceita `UPDATE`, `DELETE` nem `TRUNCATE`, e o papel somente leitura não escreve | `AuditImmutabilityTests` |
| Privilégios por papel, e o Worker lê só as cinco colunas | `RolePrivilegesTests`, `WorkerAuditLogGrantTests` |
| Nenhum dado pessoal nos `details` de uma execução completa | `AuditDetailsPrivacyTests` |
| `keys.version_activated` uma vez por versão ativa | `KeyActivationAuditTests` |
| Recifragem grava `pii.decrypted` e `pii.rewrapped` | `KeyRewrapServiceTests`, `PostgresAccountKeyRewrapperTests` |
| A consulta de saldo e de extrato deixa o registro estruturado | `ReadAuditLogTests` |
