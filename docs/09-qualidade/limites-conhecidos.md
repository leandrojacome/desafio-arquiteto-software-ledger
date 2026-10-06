# Limites conhecidos

O ledger é a solução de um desafio técnico de arquitetura, construída e exercitada numa máquina de desenvolvimento, com PostgreSQL e RabbitMQ reais em contêineres. Invariantes, concorrência, idempotência e recuperação da queda de uma instância têm teste que os prova. O resto são premissas de dimensionamento ou comportamento descrito, sem medida nem execução. Cada seção abaixo diz o que foi verificado localmente, como eu provaria o que falta e o que mudaria no desenho se a hipótese caísse.

## Vazão e latência nas metas

As metas são 2.000 lançamentos por segundo no total, 50 por segundo na mesma conta, 10.000 consultas de saldo por segundo, p99 de 150 ms na escrita e de 50 ms no saldo. Vêm dos [requisitos não funcionais](../02-contexto-e-requisitos/requisitos-nao-funcionais.md), a conta de [capacidade e escala](../08-resiliencia-e-operacao/capacidade-e-escala.md) parte delas, e nenhuma foi medida. O k6 roda em escala reduzida, na mesma máquina da pilha: mostra que as rotas respondem e que os invariantes se mantêm sob carga, não que o sistema aguenta a escala. O `QueryPlanTests` confirma o saldo em um instante como busca por índice, só que em tabela pequena.

Para provar, rodaria o cenário `mixed` com `LOAD_SCALE=1` em ambiente dedicado, com a topologia de produção, 20 milhões de contas e 1 bilhão de lançamentos, em três passagens de uns 30 minutos, sustentando as vazões sem `dropped_iterations`, com falha abaixo de 0,05% e p99 dentro das metas.

Se a vazão não fechar, acho o gargalo (lock da conta, pool, WAL, disco) e ajo nele, e a meta só muda se ele for estrutural. Se o saldo passar de 50 ms, confiro o plano de execução e o pool, e só então considero réplica de leitura para `asOf` antigo e cache do saldo atual, o que reabre o [documento de arquitetura 0009](../03-principios-e-decisoes/documento-arquitetura/0009-sem-cache-na-v1.md).

## Failover, RPO zero e RTO de 15 minutos

Um PostgreSQL único não cumpre RPO zero nem RTO de 15 minutos. O desenho prevê um primário com standby síncrono, promoção automática e o primário antigo isolado antes da promoção ([topologia de produção](../04-modelos-c4/implantacao-producao.md)). O Compose sobe uma instância só. O `CommitCuttingProxyTests` corta a resposta do commit e mostra que repetir a chamada devolve o mesmo lançamento, e o `PostgresOutageE2ETests` mata o contêiner do banco no meio de um fluxo de escritas e confere que nenhum lançamento confirmado se perde. Isso cobre a queda de uma instância, não a promoção de um standby.

O ensaio teria carga de fundo e um escritor que guarda cada `entryId` aceito, como o `ContinuousWriter` dos testes de ponta a ponta. Eu derrubaria o primário sem desligamento limpo e conferiria no novo primário que todo `entryId` com 201 existe. Uma partição de rede mostraria se o primário isolado ainda confirma commit, e um relógio de standby atrasado, a monotonicidade do `recorded_at` agindo ([documento de arquitetura 0018](../03-principios-e-decisoes/documento-arquitetura/0018-monotonicidade-do-recorded-at-e-janela-de-acomodacao.md)). Com 20 e 50 ms a mais no commit do standby, o teto de uma conta quente cai, pela conta de capacidade, para uns 38 e uns 18 lançamentos por segundo, e o excesso deve virar 503, sem lançamento perdido. O critério é RPO zero em todas as repetições e escrita de volta em até 15 minutos. A disponibilidade de 99,95% só se mede em operação.

Lançamento com 201 ausente depois da promoção, ou dois primários, é falha bloqueante: reviso `synchronous_commit` e `synchronous_standby_names` e repito antes de qualquer tráfego real. RTO entre 15 e 30 minutos pede ajuste de detecção e de promoção, e acima disso reabro a escolha do orquestrador, entre um serviço gerenciado com failover nativo e outro orquestrador. O RTO não cobre corrupção lógica, que se resolve com restauração pontual. Estimo horas para restaurar o volume premissado, mas nenhuma restauração foi cronometrada.

## Pico, conta quente e calibração dos limites

O pool de escrita de 7 conexões por instância, os limitadores de concorrência de 16, 16 e 8 ([documento de arquitetura 0027](../03-principios-e-decisoes/documento-arquitetura/0027-limites-de-taxa-e-de-concorrencia-em-cadeia.md)), o `lock_timeout` de 1 segundo ([documento de arquitetura 0011](../03-principios-e-decisoes/documento-arquitetura/0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md)), a janela de acomodação de 5 segundos e a meta de 5 segundos de p99 para a publicação do outbox ([documento de arquitetura 0008](../03-principios-e-decisoes/documento-arquitetura/0008-rabbitmq-entrega-ao-menos-uma-vez.md)) são valores de mesa. Os testes provam que os limites de taxa, de concorrência e de pool atuam (`RateLimitBehaviorTests`, `SaturatedWritePoolTests`), e o `EventLatencyE2ETests` mede o atraso com mil lançamentos. Nada exercita uma conta a 100 lançamentos por segundo contra a topologia de produção, nem um pico de três vezes o perfil.

Para calibrar, compararia o p99 das demais escritas com e sem a conta quente a 100 por segundo, e ele não pode subir mais de 10%. O da própria conta quente deve ficar em até 150 ms. Mediria o p99 de `published_at - created_at` em `outbox_messages`, com 2.000 mensagens por segundo e duas instâncias do Worker, e o p99,99 de `ledger.entry.duration`, para ver se a janela de 5 segundos tem folga. Num pico de três vezes o perfil por cinco minutos, toda recusa deve ser 429 ou 503 com `Retry-After`, nunca 500, e tudo deve voltar ao normal sozinho.

Se a conta quente degradar as demais, a ordem é pool de escrita de 12 (cabe nos 200 de `max_connections`), limite de concorrência por conta e, por último, microlote por conta. Atraso acima de 5 segundos se ataca com `Outbox:IdlePollMs`, `Outbox:BatchSize` ou mais instâncias do Worker. Se a janela de acomodação sobrar, reduzo `Ledger:Balance:SettlingWindowSeconds` a duas vezes o p99,99 medido, com mínimo de 1 segundo.

## Volume real de tabela e rotinas de fundo

A conta de capacidade parte de 200 lançamentos por segundo de média, 20 milhões de contas e 430 bytes por lançamento com índices, de onde saem uns 2,7 TB por ano. Os tamanhos são estimativa: uma amostra sintética pequena, sem texto livre, ficou abaixo deles. A auditoria de leitura, um evento de log por consulta, soma uns 13 GB por dia a um décimo do pico, por cálculo, e a conformidade não disse se o log completo é exigido. A conferência de integridade completa, estimada em 8 a 15 minutos com 20 milhões de contas, também foi extrapolada.

Eu pediria aos sistemas de Pix, cartões e conciliação a média dos últimos 90 dias e, depois de 30 dias de operação, mediria com `pg_total_relation_size` as tabelas `ledger_entries`, `idempotency_keys`, `outbox_messages` e `account_balances`. Também mediria os bytes de `Ledger.Audit` sob a carga de leitura e rodaria a conferência completa durante um `mixed` a 50% da escala, que deve terminar em até 12 horas sem pôr o p99 de escrita mais de 10% acima.

Se a média real bater com a premissa, o particionamento mensal já é pré-requisito do lançamento em escala, e com média acima de 600 por segundo ele passa a exigir camadas de armazenamento ([Evolução futura](../11-evolucao/evolucao-futura.md)). Se a auditoria passar do que a conformidade aceitar, agrego o saldo atual por cliente, conta e minuto e mantenho completos extrato e saldo histórico. Conferência acima de 24 horas passa a ler de réplica, e acima de 26 reabre o [documento de arquitetura 0026](../03-principios-e-decisoes/documento-arquitetura/0026-conferencia-de-integridade-com-trava-por-modo.md).

## Disco cheio

A causa mais provável é acúmulo invisível, como WAL retido por um slot de replicação inativo. Com o volume de dados cheio, as escritas falham com `53100` e as leituras seguem. Com o WAL cheio, o PostgreSQL encerra e só volta com espaço. Em ambos o confirmado está seguro, porque o 201 só sai depois de o WAL ir para o disco ([cenário 13](../08-resiliencia-e-operacao/cenarios-de-falha-9-a-16.md)). Só o mapeamento de `53100` para 503 sem nova tentativa foi verificado (`DependencyUnavailableLogTests`, `PostgresRetryPredicateTests`). Nenhum teste enche um disco.

O ensaio põe o diretório de dados num volume de memória de poucos megabytes: gravar até o banco recusar e conferir só 503 com `Retry-After`, todo `entryId` com 201 existindo depois de reiniciar e a escrita voltando, sem reiniciar a API. Um 500 na escrita é defeito de mapeamento, e lançamento confirmado perdido é falha crítica.

## Cofre de chaves e emissor de tokens

O provedor de chaves do documento do titular em produção (`Security:Pii:Provider` igual a `Directory`) lê uma pasta de segredos, e não existe adaptador para um cofre externo. Com a fonte falhando, só `POST /v1/accounts` responde 503 e a readiness fica `Degraded` ([documento de arquitetura 0019](../03-principios-e-decisoes/documento-arquitetura/0019-readiness-nao-depende-do-provedor-de-chaves.md)), o que os testes provam só contra um provedor que falha (`ApiReadinessWithDatabaseTests`, `KeyProviderHealthCheckTests`). Escreveria o adaptador para `IKeySetSource` e repetiria os testes contra o cofre ou um emulador, depois sob carga com a rede ao cofre bloqueada. Se o cofre exigir chamada por operação, o desenho ganha um cache de chaves no processo.

O emissor de tokens segue o mesmo raciocínio: os testes usam um emissor falso (`FakeIssuer`), e o `JwksCacheTests` mostra que, com ele fora, tokens de chave conhecida seguem aceitos, chave desconhecida é recusada e a readiness não muda. Falta repetir com o emissor real, sob carga.

## TLS efetivo

A subida recusa, fora de `Development` e `Testing`, `Postgres:SslMode` diferente de `VerifyFull` e `RabbitMq:UseTls` desligado (`PostgresOptionsValidatorTests`, `MessagingOptionsValidationTests`). Isso valida configuração: nenhum teste abre uma conexão TLS real, e o Compose roda com `Postgres__SslMode` em `Disable` e `RabbitMq__UseTls` em `false`.

O ensaio sobe PostgreSQL e RabbitMQ com certificados de uma CA de teste e confere em `pg_stat_ssl` as conexões dos papéis `ledger_api` e `ledger_worker`, e no RabbitMQ a conexão cifrada do Worker. Repete com certificado de outra CA e com nome de servidor errado, que devem ser recusados. Conexão em claro aceita é defeito de segurança, a corrigir antes de qualquer implantação.

## Prazo de reenvio dos chamadores

As chaves de idempotência ficam 35 dias (`Idempotency:RetentionDays`, [documento de arquitetura 0017](../03-principios-e-decisoes/documento-arquitetura/0017-retencao-de-35-dias-das-chaves-de-idempotencia.md)), e depois da poda a mesma chave cria um lançamento novo. A janela precisa cobrir o maior intervalo entre o primeiro envio e a última nova tentativa de qualquer chamador, e quem sabe esse número são os donos dos sistemas de Pix e de cartões. Ninguém o confirmou. Perguntaria a cada um, por escrito. Se todos disserem até 7 dias, reduzo o prazo. Se algum passar de 35, cada dia a mais de prazo custa uns 2,6 GB de chaves, e as saídas são uma chave de negócio estável no chamador ou uma tabela fria para as antigas.

## Encerramento planejado atrás de um balanceador

O ledger não tem janela de drenagem própria: no `SIGTERM` a verificação `shutdown` fica `Unhealthy` e o servidor deixa de aceitar conexões no mesmo instante. Tirar a instância do balanceamento antes do sinal é papel do orquestrador, e nenhum foi exercitado ([encerramento e reinício](../06-fluxos/encerramento-e-reinicio.md)). Para provar, usaria duas instâncias da API atrás de um balanceador, 20% do perfil e dez encerramentos, aceitando zero 5xx e zero conexão recusada. Se falhar, a saída é uma espera configurável no próprio processo.

## Execução dos pipelines no serviço

O pipeline do Azure DevOps e o workflow do GitHub Actions ([documento de arquitetura 0016](../03-principios-e-decisoes/documento-arquitetura/0016-azure-devops-como-plataforma-de-ci-cd.md)) estão escritos. O `CiPipelineTests` e o `GitHubWorkflowTests` conferem a estrutura dos arquivos, e cada passo chama um comando que também roda na máquina de desenvolvimento. Nunca rodou o que só o serviço executa: a expansão dos modelos, a aprovação do envio das imagens e a memória e o tempo de uma máquina hospedada.

O ensaio roda em `main` e em pull request, com defeitos plantados em ramos descartáveis (um teste quebrado, uma chave de exemplo, uma queda de cobertura abaixo do piso) para ver cada porta barrar, e enviando as imagens a um registro de teste. Se o serviço recusar o YAML, corrijo e acrescento o caso ao `CiPipelineTests`. Se faltar memória ou estourar o limite de downloads do Docker Hub, o caminho é um pool maior ou um espelho das imagens.

## Leia também

- [Capacidade e escala](../08-resiliencia-e-operacao/capacidade-e-escala.md): a conta das metas.
- [Requisitos não funcionais](../02-contexto-e-requisitos/requisitos-nao-funcionais.md): as metas.
