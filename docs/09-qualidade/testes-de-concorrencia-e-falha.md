# Testes de concorrência e de falha

São os testes que mais importam num ledger. Os de concorrência (C1 a C7) provam que saldo, idempotência e estorno sobrevivem a requisições simultâneas, e os de falha (F1 a F10), que uma queda no meio do caminho não deixa lançamento pela metade nem duplica dinheiro. As camadas estão em [Estratégia de testes](estrategia-de-testes.md), e o que cada falha significa para quem chama e para quem opera, em [Cenários de falha](../08-resiliencia-e-operacao/cenarios-de-falha.md).

## Regras comuns

Os testes de concorrência ficam em `Ledger.Api.IntegrationTests`, com `[Trait("Category", "Concurrency")]`, e rodam contra um PostgreSQL real, nunca contra dublê, com três cuidados. A largada é sincronizada: o `ParallelGate.RunAsync` cria todas as chamadas, faz cada uma esperar o mesmo `TaskCompletionSource` e as libera de uma vez, para as requisições colidirem de fato. O pool de threads é aquecido (`ThreadPool.SetMinThreads(256, 256)`), senão o próprio teste serializaria as chamadas e passaria por sorte. E as asserções são sobre invariantes, nunca sobre tempo nem ordem de chegada, porque um teste que depende de quem ganhou a corrida é instável.

Cada cenário se repete `CONCURRENCY_ITERATIONS` vezes (`ConcurrencySettings.RepeatAsync`) para aumentar a chance de pegar uma corrida rara: 3 por padrão, 1 no perfil rápido e 25 na varredura exaustiva. Os testes têm dois níveis. O primeiro roda o código de produção sem a API, com os handlers sobre `PostgresUnitOfWork`, os repositórios e os leitores compostos pelo `LedgerHost`, contra o PostgreSQL do Testcontainers (`postgres:16`, papéis do `init-roles.sh`, `synchronous_commit` ligado). Na maioria deles o pool de escrita é alargado (`LedgerHost.WideWritePool`: 64 conexões e `LockTimeoutMs` de 10.000), para a contenção medida ser a da linha da conta e não a do pool. O `SaturatedWritePoolTests` usa o padrão e dispara 100 débitos na mesma conta: só aceita 201, 422 ou uma falha que o classificador transforma em 503, sem rastro parcial. O segundo nível passa pelas rotas HTTP, em `Writes/Concurrency` e `Reads`, com a API em memória.

## Invariantes verificados

O `InvariantVerifier` executa quatro consultas SQL, em `tests/Ledger.Api.IntegrationTests/Invariants/`, que devolvem zero linhas num estado saudável. Cada teste de concorrência termina com `ConcurrencyScenarios.AssertConsistentAsync`, que as chama com a fonte administrativa do banco, e a conferência de integridade do Worker faz o equivalente de forma contínua ([Fluxo: conferência de integridade](../06-fluxos/conferencia-de-integridade.md)).

| Consulta | O que acusa |
|---|---|
| `balance-equals-sum-of-entries.sql` | Saldo corrente diferente da soma assinada dos lançamentos |
| `balance-matches-last-entry-and-floor.sql` | Saldo diferente do `balance_after` do último lançamento, versão ou `last_entry_id` fora de sincronia com ele, ou saldo abaixo de `-overdraft_limit` |
| `balance-after-chain-closes.sql` | Cadeia de `balance_after` que não fecha, lançamento a lançamento |
| `version-and-recorded-at-sequence.sql` | Lacuna na versão da conta, ou `recorded_at` que não cresce estritamente com a versão |

## Testes de concorrência

| Id | Cenário | Contra o código de produção | Pela API |
|---|---|---|---|
| C1 | Lançamentos paralelos na mesma conta preservam a soma | `ParallelEntriesPreserveSumTests` (16, 64 e 256 lançamentos, semente fixa) | `ParallelEntriesPreserveSumHttpTests` |
| C2 | Débitos concorrentes nunca deixam o saldo abaixo do limite | `ParallelDebitsTests` (dez de 20,00 contra 100,00, cem de 10,00 contra 500,00, cem de 30,00 contra 1.000,00, cem de 30,00 com limite, e um observador do saldo) | `ParallelDebitsHttpTests` e `ParallelEntriesPreserveSumHttpTests` |
| C3 | A mesma `Idempotency-Key` em paralelo gera um lançamento só | `SameIdempotencyKeyTests` | `SameIdempotencyKeyHttpTests` |
| C4 | Estornos concorrentes do mesmo lançamento | `ParallelReversalsTests` | `ParallelReversalsHttpTests` |
| C5 | O saldo em um instante bate com a cadeia depois de escritas concorrentes | `AsOfMatchesChainTests` e `RecordedAtMonotonicityTests` | `StatementMatchesBalanceTests` e `StableAsOfTests` |
| C6 | Uma conta travada não bloqueia as outras | `LockedAccountTests` | `LockedAccountHttpTests` |
| C7 | Muitas contas ao mesmo tempo | `ManyAccountsTests` (20 contas com 10 lançamentos e 50 com 20) | `ParallelDebitsHttpTests` e `ParallelEntriesPreserveSumHttpTests` |

**C1.** A conta abre com um crédito de 100.000,00 e cada chamada é um crédito ou débito de valor sorteado com semente fixa (`ConcurrencyScenarios.MixedRequests`). Todas as respostas são de sucesso, e a conta termina com `chamadas + 1` lançamentos de versões consecutivas, saldo igual à abertura mais a soma assinada dos pedidos, uma linha de outbox por lançamento e as consultas de invariante vazias.

**C2.** Sem limite, 100 débitos simultâneos de 30,00 contra 1.000,00 aceitam exatamente 33 e terminam em 10,00. Com limite de cheque especial de 500,00, o mesmo cenário aceita 50 e termina exatamente em `-500,00`, o que testa a fronteira do `>=`, e cem débitos de 10,00 contra 500,00 aceitam exatamente 50 e deixam zero. Um observador consulta o saldo durante toda a rajada, e o menor valor visto não pode ser menor que `-limite`. É a prova de que a decisão de aceitar ou recusar está no `UPDATE` condicional ([Fluxo: registro de lançamento](../06-fluxos/registro-de-lancamento.md)).

**C3.** Quarenta chamadas idênticas disparadas juntas produzem um lançamento novo e 39 repetições, com o mesmo `entryId` em todas as respostas, o saldo debitado uma vez e uma linha de outbox e de chave por lançamento. Vinte chamadas com a mesma chave e valores diferentes produzem um sucesso e 19 recusas por chave reutilizada. A unicidade é por `(conta, chave)`: a mesma chave em duas contas gera dois lançamentos, e a mesma chave de outro chamador é conflito, nunca repetição. O `RefusedDebit_DoesNotConsumeTheIdempotencyKey` prova que a recusa por saldo desfaz a reserva da chave ([Idempotência e hash canônico](../05-contratos/idempotencia-e-hash-canonico.md)).

**C4.** Vinte pedidos de estorno do mesmo lançamento, com chaves distintas, produzem um sucesso e 19 recusas `ENTRY_ALREADY_REVERSED`, e o saldo reflete o estorno uma vez. Quando o crédito a estornar é todo o saldo, quem perde a corrida recebe `ENTRY_ALREADY_REVERSED` e nunca `INSUFFICIENT_FUNDS`. Estornar um crédito cujo dinheiro já foi gasto dá `INSUFFICIENT_FUNDS` e deixa o original reversível, e estornar o lançamento de outra conta dá 404.

**C5.** Depois de 100 lançamentos paralelos, para uma amostra de 50, a consulta `asOf` no `recorded_at` do lançamento devolve o `balance_after` dele, e um microssegundo antes devolve o do anterior. O teste existe porque, se o `recorded_at` fosse tomado no início da transação e não depois de a linha de `account_balances` ser travada, dois lançamentos concorrentes poderiam ter a ordem dos relógios invertida em relação à da cadeia, e uma consulta entre eles devolveria um saldo que já inclui o posterior ([documento de arquitetura 0013](../03-principios-e-decisoes/documento-arquitetura/0013-tempo-do-saldo-registrado-em-vs-ocorrido-em.md)). O `RecordedAtMonotonicityTests` prova que, mesmo com o relógio guardado adiantado, o `recorded_at` cresce de um em um microssegundo.

**C6 e C7.** No C6, uma conexão direta faz `SELECT ... FOR UPDATE` na linha de `account_balances` da conta A. O lançamento na conta A espera e o da conta B termina, e a verificação é por ordem (`Task.WhenAny` devolve a chamada de B enquanto a de A não terminou), não por tempo. Liberada a transação, o de A conclui, o que prova que a linha serializa as escritas daquela conta e só dela. O C7 dispara juntos, com créditos e débitos misturados, vinte contas com 10 lançamentos e cinquenta com 20, e confere as invariantes de todas, o que cobre a interferência entre contas que os testes de conta única não veem.

## Testes de falha

Estes testes derrubam coisas de propósito. Os que cabem no processo de teste ficam em `Ledger.Api.IntegrationTests`, e os que derrubam contêineres do Compose ficam em `Ledger.EndToEnd.Tests`, todos com `[Trait("Category", "Resilience")]`. O comportamento de cada falha está na página de cenários, e aqui ficam o teste e a asserção.

| Id | Cenário | Teste | Nível |
|---|---|---|---|
| F1 | Conexões do banco mortas no meio do tráfego | `WriteFailureTests` | Integração |
| F2 | Trava mais longa que o prazo | `WriteFailureTests` | Integração |
| F3 | Banco parado e recuperado, sem reiniciar a API | `PostgresOutageE2ETests` e `ReadsResilienceE2ETests` | Fim a fim |
| F4 | Banco derrubado à força e durabilidade | `PostgresOutageE2ETests` | Fim a fim |
| F5 | Broker fora do ar | `BrokerOutageTests` e `BrokerOutageE2ETests` | Integração e fim a fim |
| F6 | Worker morto entre publicar e marcar | `PublisherKilledBetweenPublishAndMarkTests` | Integração |
| F7 | Dois Workers ao mesmo tempo | `TwoPublishersTests` | Integração |
| F8 | API morta no meio da carga | `ApiKillE2ETests` | Fim a fim |
| F9 | Esquema atrasado em relação ao código | `ApiReadinessWithDatabaseTests` e `WorkerHealthSignalsTests` | Integração |
| F10 | Emissor de tokens fora do ar | `JwksCacheTests` | Integração |

**F1.** Duzentos lançamentos com chaves distintas rodam enquanto uma conexão administrativa executa `pg_terminate_backend` nas conexões de escrita da API que estão em uso, até três vezes, e as chamadas que falham são repetidas com a mesma chave até terem sucesso. No fim a conta tem um lançamento, uma linha de outbox e uma chave por pedido, o saldo é a abertura menos 200,00 e as consultas de invariante voltam vazias: a transação é atômica e a repetição não duplica. O `CommitCuttingProxyTests` complementa com um proxy que corta a resposta do `COMMIT` enquanto o PostgreSQL confirma, e a repetição devolve o lançamento já gravado. O `UnitOfWorkCancellationTests` prova que o cancelamento do token solta a trava da conta, e o `PostgresUnitOfWorkRetryTests` fixa a retentativa por SQLSTATE.

**F2.** Uma sessão direta segura a trava da linha de saldo por mais tempo que `LockTimeoutMs`. A escrita falha com `55P03`, registra o evento `LockTimeoutExceeded` e nenhum `TransientDatabaseFailure`, e não deixa rastro em lançamentos, chaves nem outbox. Repetida depois da liberação, a mesma chave cria um único lançamento. O mesmo arquivo prova que uma trava longa em `outbox_messages` atrasa o lançamento sem perdê-lo nem duplicá-lo.

**F3.** Três escritores contínuos mandam créditos de 1,00 e o PostgreSQL é parado depois de 30 aceitos. Toda resposta tem de ser 201 ou 503 `SERVICE_UNAVAILABLE` com `Retry-After: 1`, e cada 503 chega em menos de 5 segundos. `/health/ready` vira 503 com `Retry-After: 5` e `/health/live` segue 200. Com o banco de volta, a API responde sem reiniciar (o teste confere que o horário de início do contêiner da API não mudou) e, repetidas as chaves, as que deram 201 voltam como repetição, as que deram 503 são aceitas uma vez e o banco termina com um lançamento por chave, saldo igual à soma e cadeia fechada. As esperas têm prazo de 30 a 60 s, porque a sonda de readiness fica em cache (`Resilience:Health:CacheSeconds`, 5 s por padrão) e demora a virar 503.

**F4.** Durante um fluxo contínuo de lançamentos, o teste guarda o `entryId` de cada 201, faz `docker compose kill` no PostgreSQL (SIGKILL, sem desligamento limpo) e o sobe de volta. Cada `entryId` confirmado antes da queda precisa existir depois da recuperação. É a versão local do RPO zero para lançamentos confirmados, e depende de o volume persistir e do `fsync` padrão. O RPO zero com standby síncrono e failover, porém, não foi exercitado ([Limites conhecidos](limites-conhecidos.md)).

**F5.** Com o broker parado, as escritas seguem em 201, o `/health/ready` da API segue 200, o Worker fica `Degraded` com status 200 e as linhas não publicadas crescem. Com o broker de volta, o acúmulo zera, e um consumidor de teste confere que os `message_id` distintos somam o número de lançamentos: duplicata é aceitável, perda não. Na integração, o broker também é pausado em vez de parado: as publicações estouram o prazo, o circuito abre, o Worker deixa de reivindicar mensagens, as que já tinha reivindicado voltam sem gastar tentativa e tudo drena quando o circuito fecha. No fim a fim, 100 lançamentos dão 201 sem piorar o tempo: o mais lento fica abaixo do maior valor entre 1 s, multiplicado pelo fator de latência, e dez vezes o p99 de 20 créditos medidos antes da queda. Zerar o acúmulo leva a reconexão (espera de até 30 s) e os 30 s de circuito aberto, por isso o teste dá 120 s.

**F6 e F7.** Um publicador de teste, que decora o real, lança exceção depois de publicar e antes de marcar, simulando a morte do processo, e um segundo assume. A mensagem tem de ser entregue duas vezes com o mesmo `message_id`, sem perda, o que prova a entrega ao menos uma vez ([documento de arquitetura 0008](../03-principios-e-decisoes/documento-arquitetura/0008-rabbitmq-entrega-ao-menos-uma-vez.md)). O `PublisherShutdownTests` prova que o desligamento limpo publica e marca o lote em curso e não reivindica outro, e o `WorkerRestartE2ETests` reinicia o Worker do Compose no meio das escritas e confere que todo lançamento tem o seu evento, com uma duplicata aceita. No F7, o `TwoPublishersTests` põe dois publicadores sobre o mesmo backlog e exige cada mensagem publicada uma vez, sem sobreposição de lotes, o que prova o `FOR UPDATE SKIP LOCKED` ([Fluxo: publicação do outbox](../06-fluxos/publicacao-do-outbox.md)).

**F8 a F10.** No F8, o `ApiKillE2ETests` mata a API com `kill` no meio de três escritores contínuos: as requisições em voo falham por conexão perdida e nunca por outro status, e com a API de volta, repetidas as chaves, as confirmadas voltam como repetição, as perdidas são aceitas uma vez e as consultas de invariante voltam vazias. No F9, uma API apontada para um banco sem a última migração responde 503 em `/health/ready` e 200 em `/health/live` e volta a 200 quando o esquema chega, o que impede uma implantação com migração atrasada de receber tráfego ([Fluxo: migração do esquema](../06-fluxos/migracao-do-esquema.md)), e o Worker se comporta igual. No F10, depois da primeira busca do conjunto de chaves o teste derruba o emissor falso: tokens de chaves conhecidas seguem válidos, o de chave desconhecida é recusado e a readiness não muda ([Autenticação e autorização](../07-consistencia-e-seguranca/autenticacao-e-autorizacao.md)).

## O que estes testes não provam

O deadlock verdadeiro não é provocado: prova-se a retentativa por SQLSTATE (`PostgresUnitOfWorkRetryTests`, `PostgresRetryPredicateTests`) e não a ocorrência do ciclo. Os testes de falha provam a recuperação de instâncias isoladas em Docker. O que ficou sem exercício, como o disco cheio, o failover com standby e o RTO de 15 minutos, está em [Limites conhecidos](limites-conhecidos.md), com o jeito de provar cada um. A tabela de garantias e do teste que prova cada uma está em [Modelo de consistência](../07-consistencia-e-seguranca/modelo-de-consistencia.md).
