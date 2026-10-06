# Requisitos não funcionais

Além do que o ledger faz, importa como ele se comporta: vazão, latência, disponibilidade, durabilidade, segurança, observabilidade e capacidade de mudar. As metas de vazão, latência, disponibilidade e recuperação são premissas de dimensionamento, não medidas de produção. O que ainda não foi medido está dito mais abaixo.

## Requisitos

Os números de NFR-01 a NFR-07 vêm das premissas de dimensionamento, não de medição do banco. O NFR-13 é uma meta minha, e o NFR-16 é uma meta de segurança de transporte que ainda não tem teste.

| ID | Requisito e meta | Como se verifica |
|---|---|---|
| NFR-01 | Vazão de escrita: 2.000 lançamentos por segundo no total e 50 por segundo na mesma conta | Carga k6 nos cenários `write-spread` e `write-hot-account`. No Compose de teste rodam por padrão em um décimo da escala (`LOAD_SCALE`) e verificam os invariantes. A escala cheia não foi executada |
| NFR-02 | Vazão de leitura de saldo: 10.000 consultas por segundo | k6 nos cenários `balance-read` e `mixed`, com saldo atual e `asOf`, também em um décimo da escala |
| NFR-03 | Latência de escrita: p99 até 150 ms | Limiar do k6 e `EntriesLatencyE2ETests`, que faz 200 créditos em sequência contra a pilha do Compose. Não há standby síncrono nem carga de pico nessa medida |
| NFR-04 | Latência de saldo, atual ou histórico: p99 até 50 ms | Limiar do k6 e `ReadsLatencyE2ETests`, sem o volume de tabela premissado |
| NFR-05 | Disponibilidade de 99,95%, cerca de 21,6 minutos de indisponibilidade em 30 dias | Taxa de sucesso contra o orçamento de erro. Só se demonstra com operação medida por um período, e nada foi medido |
| NFR-06 | RPO zero para lançamento confirmado | O 201 só sai depois do commit (`CommitCuttingProxyTests`, `PostgresOutageE2ETests`), então nenhuma escrita confirmada se perde na queda do banco. O RPO zero com failover depende de standby síncrono, que o repositório não sobe |
| NFR-07 | RTO de até 15 minutos | Ensaio de restauração e de promoção de instância, que depende da topologia de produção e não foi feito. O que existe é o teste de queda e retorno de uma instância do banco |
| NFR-08 | Retenção de 10 anos, sem remoção física de lançamentos | Gatilho, privilégios e `LedgerEntriesAreNeverMutatedTests`. O prazo é premissa, a confirmar com o jurídico |
| NFR-09 | Consistência sob concorrência: zero atualização perdida e zero saldo abaixo do limite | Testes de concorrência com banco real e conferência de integridade (FR-09, FR-20) |
| NFR-10 | Segurança: JWT com escopos, recusa de subir fora do desenvolvimento sem TLS configurado com o banco e com o broker, documento cifrado com AES-256-GCM, nada sensível em log | `RouteScopeMatrixTests`, `PostgresOptionsValidatorTests`, `MessagingOptionsValidationTests`, `AccountDocumentProtectionTests`, `LogLeakTests`. Os validadores recusam `Postgres:SslMode` diferente de `VerifyFull` e `RabbitMq:UseTls` desligado fora de `Development` e `Testing`. O TLS efetivo é o NFR-16, e o TLS entre chamador e API é terminado pela infraestrutura de entrada, fora do repositório |
| NFR-11 | Observabilidade: logs estruturados, traces e métricas, `X-Correlation-Id` de ponta a ponta | `CorrelationIdTests`, `TelemetryTests`, `InstrumentCatalogTests` |
| NFR-12 | Resiliência: broker fora do ar não derruba a escrita, banco fora do ar responde 503 com `Retry-After` | `BrokerOutageE2ETests` e `PostgresOutageE2ETests`, com parada real dos contêineres |
| NFR-13 | Atraso de publicação de eventos: p99 de até 5 segundos entre o commit e a publicação | Idade da mensagem pendente mais antiga e `EventLatencyE2ETests`, que mede 1.000 lançamentos sem a carga das premissas |
| NFR-14 | Reprodutibilidade: subir tudo com um comando de Docker Compose, migrações aplicadas | `docker compose up -d --build --wait` e `ComposeSmokeE2ETests`, que confere `/health/live` e `/health/ready` da API e do Worker. A prontidão da API exige o esquema compatível com o código, então só responde 200 com as migrações aplicadas. Com `LEDGER_E2E_PROVISION=true`, o teste fim a fim sobe e derruba a própria pilha |
| NFR-15 | Rastreabilidade: cada requisito funcional tem ao menos um teste automatizado | `RequirementsMatrixTests` sobre [Rastreabilidade de requisitos](rastreabilidade.md) |
| NFR-16 | Segurança do transporte: TLS efetivo da API e do Worker com o PostgreSQL (`VerifyFull`) e do Worker com o RabbitMQ (AMQPS), aceitando o certificado válido e recusando o inválido | Nenhum teste abre uma conexão TLS real, e o Compose roda com `Postgres__SslMode` em `Disable` e `RabbitMq__UseTls` em `false` |

O RTO de 15 minutos consome quase 70% do orçamento mensal de indisponibilidade de 99,95%, então um incidente que usasse o RTO inteiro deixaria pouco espaço para outro no mês. Por isso o alvo operacional do failover é bem menor que o RTO (90 segundos no cenário DIS-2). Divido o orçamento de 21,6 minutos, em ordem de grandeza, em dez minutos para o banco (failover e manutenção), cinco para deploy e rede e seis de reserva. O indicador de disponibilidade e o que conta contra ele estão em [Indicadores, objetivos e alertas](../08-resiliencia-e-operacao/slos-e-alertas.md).

A meta de 50 lançamentos por segundo na mesma conta existe porque o caso difícil de um ledger é a conta muito movimentada, como a de um lojista ou de repasse de um parceiro, em que todas as escritas disputam o mesmo saldo. Na configuração padrão ela também é um limite aplicado: a cota `RateLimiting:WritePerAccount` repõe 50 permissões por segundo, com rajada de 100 ([Limites de taxa e de concorrência](../08-resiliencia-e-operacao/limites-de-taxa-e-concorrencia.md)). O cenário de conta quente do k6 gera 100 por segundo e roda com a cota elevada do Compose de teste.

### O que ainda não foi medido

Os cenários de carga rodam por padrão em um décimo da escala. Isso exercita cenários, limiares e invariantes de ponta a ponta, mas diz pouco sobre a capacidade nas metas de NFR-01 a NFR-04. A disponibilidade só se demonstra com pelo menos 30 dias de operação contra o orçamento de erro. O RPO zero com failover e o RTO de 15 minutos supõem um primário com standby de replicação síncrona e promoção automática, topologia que o repositório não sobe ([Implantação: topologia de produção](../04-modelos-c4/implantacao-producao.md)). O NFR-13 é uma meta minha, para dar expectativa a quem consome eventos, e pede medição sob a carga das premissas. No NFR-10, o que se mostra é que a configuração sem TLS é recusada fora do desenvolvimento, e não que a conexão negociada é cifrada, que é o NFR-16.

Como cada um desses pontos seria medido, e o que mudaria no desenho se a hipótese caísse, está em [Limites conhecidos](../09-qualidade/limites-conhecidos.md).

## Requisitos arquiteturalmente significativos

Um requisito é significativo para a arquitetura quando, se fosse removido, o desenho poderia ser bem mais simples. A tabela lista os que passaram nesse teste e o que cada um força.

| ID | Requisito | Origem | O que força no desenho |
|---|---|---|---|
| ASR-01 | Lançamento confirmado nunca se perde | NFR-06 | A resposta só sai depois do commit, e em produção o commit espera um standby síncrono |
| ASR-02 | Repetir a mesma operação não duplica dinheiro | BR-11 | `Idempotency-Key` com unicidade `(conta, chave)` no banco, e não na memória da aplicação |
| ASR-03 | O saldo nunca passa do limite, nem sob concorrência | BR-06, BR-10 | `UPDATE` condicional na linha da conta, em vez de ler, calcular e gravar |
| ASR-04 | Saldo em qualquer instante é uma busca por índice | BR-08 | `balance_after` gravado em cada lançamento |
| ASR-05 | Histórico imutável por 10 anos | BR-01, BR-18 | Só inserção, estorno como novo lançamento e um volume que obriga a pensar em particionamento |
| ASR-06 | Escrever não depende do RabbitMQ | BR-15 | Mensagem na caixa de saída dentro da mesma transação do lançamento |
| ASR-07 | O documento do titular nunca fica em claro | BR-21 | Cifra AES-256-GCM, índice cego e um caminho do dinheiro que não precisa da chave |
| ASR-08 | Degradação previsível sob estresse | NFR-12 | Timeouts, isolamento entre leitura e escrita e 503 com `Retry-After` em vez de fila escondida |
| ASR-09 | Divergência entre as tabelas é detectável | BR-17 | Conferência de integridade periódica no Worker |
| ASR-10 | Toda requisição é rastreável de ponta a ponta | NFR-11, BR-19 | `X-Correlation-Id` propagado para logs, traces, linha do lançamento e mensagens |

Os dois que mais pesam são ASR-03 e ASR-06. O primeiro decide como a escrita é feita e, sozinho, elimina locks explícitos, filas por conta e transações longas. O segundo decide onde fica a fronteira entre o forte e o eventual, e impede que uma indisponibilidade do broker vire indisponibilidade do ledger. A ordem de prioridade quando dois atributos competem está em [Princípios de arquitetura](../03-principios-e-decisoes/principios-de-arquitetura.md).

## Cenários de qualidade

Cada cenário segue o formato estímulo, resposta e medida, com a verificação ao lado. O prefixo indica o atributo: CON (consistência e durabilidade), SEG (segurança), DIS (disponibilidade), DES (desempenho), OBS (observabilidade), ESC (escalabilidade), TES (testabilidade) e MAN (manutenibilidade). Onde a medida é uma meta minha e não vem das premissas de dimensionamento, o texto diz "meta". A testabilidade das regras de domínio e do relógio está em [Estratégia de testes](../09-qualidade/estrategia-de-testes.md). Os testes C1 a C7 e F1 a F10 estão em [Testes de concorrência e de falha](../09-qualidade/testes-de-concorrencia-e-falha.md).

| ID | Estímulo e ambiente | Resposta e medida | Verificação |
|---|---|---|---|
| CON-1 | 100 débitos de R$ 10,00 chegam juntos à mesma conta, com saldo de R$ 500,00 e limite zero | 50 aceitos e 50 recusados com `INSUFFICIENT_FUNDS`, saldo final R$ 0,00, cadeia de `balance_after` sem repetição nem lacuna | `ParallelDebitsTests`, `ParallelDebitsHttpTests` |
| CON-2 | Consulta com `asOf` igual a T depois de créditos, débitos e estornos | O `balance_after` do último lançamento com `recorded_at` até T, igual ao saldo recalculado somando do zero | `AsOfMatchesChainTests`, `StableAsOfTests` |
| CON-3 | O processo da API é morto entre dois comandos da transação | A transação existe inteira ou some: sem lançamento sem saldo, saldo sem lançamento ou lançamento sem mensagem | `WriteFailureTests`, `CommitCuttingProxyTests` |
| SEG-1 | Requisição sem token, com token expirado ou com escopo insuficiente | 401 ou 403 antes de executar o caso de uso, em todas as rotas exceto as de saúde | `RouteScopeMatrixTests`, `JwtAuthenticationTests` |
| SEG-2 | Criação de conta com um CPF de teste | O documento existe só cifrado no banco e mascarado nos logs, com zero ocorrências do valor em claro | `AccountDocumentProtectionTests`, `LogLeakTests`, `EntryLogLeakTests` |
| SEG-3 | O papel de banco da aplicação tenta `UPDATE` ou `DELETE` em `ledger_entries` | O banco recusa em todas as tentativas | `MigrationTests`, `RolePrivilegesTests` |
| DIS-1 | Uma instância da API é encerrada à força sob carga média | A prontidão a retira do balanceamento, as requisições em curso nela falham e as outras seguem, sem perder lançamento confirmado. Meta: menos de 0,5% de respostas 5xx em 15 segundos | `ApiKillE2ETests` confere que nada confirmado se perde. A retirada pelo balanceador e a meta de 0,5% dependem da produção e não foram exercitadas |
| DIS-2 | O primário do PostgreSQL falha, em produção com standby síncrono | Um standby é promovido, a API reconecta sozinha e a escrita volta em até 90 segundos, com RPO zero (meta) | Não exercitado: exige a topologia de produção |
| DIS-3 | O RabbitMQ fica fora do ar por uma hora, a 200 lançamentos por segundo | Escrita e consulta não mudam, os eventos acumulam na caixa de saída e saem em até 5 minutos depois da volta, com 2 instâncias do Worker | `BrokerOutageTests` e `BrokerOutageE2ETests` cobrem o acúmulo e a drenagem em escala pequena. Os 720 mil eventos da hora inteira não foram exercitados |
| DES-1 | 2.000 lançamentos por segundo no total, até 50 por segundo por conta, em 4 a 6 instâncias da API e PostgreSQL de 16 vCPU com standby síncrono (premissa) | Todos confirmados sem fila visível, p99 de 150 ms ou menos e 5xx abaixo de 0,1% | `write-spread` do k6 em um décimo da escala |
| DES-2 | 10.000 consultas de saldo por segundo, 5% delas com `asOf`, junto com DES-1 | Busca por chave ou por índice, sem somar histórico, p99 de 50 ms ou menos | `balance-read` e `mixed` do k6 em um décimo da escala |
| DES-3 | Uma conta recebe 100 lançamentos por segundo por 60 segundos, o dobro do teto premissado | Fila só na linha dessa conta, p99 da conta quente de 150 ms ou menos e p99 das outras dentro de 10% do valor sem a conta quente | `write-hot-account` do k6 |
| OBS-1 | Um chamador reporta falha e informa o `X-Correlation-Id` | Uma busca encontra os logs da API, os do Worker e o trace. Meta operacional: achar a causa em até 5 minutos | `CorrelationIdTests`, `EntryEventsE2ETests`, `OutboxTracingTests`. A busca em produção é de operação |
| OBS-2 | A caixa de saída acumula ou o Worker para | Aviso com mensagem pendente há mais de 60 segundos e chamado acima de 5 minutos, antes que qualquer consumidor perceba. Meta: os limiares são escolha minha, sem medida por trás | `OutboxTelemetryTests`, `BrokerAndLagHealthCheckTests` |
| OBS-3 | A conferência de integridade encontra divergência | Log de erro com a conta, o lançamento e a verificação que falhou, sem valor monetário, e registro na trilha. Meta: alerta crítico em até 5 minutos | `InvariantVerifierTests`, `IntegrityDetectsTamperingTests` |
| ESC-1 | A leitura de saldo dobra para 20.000 por segundo | Entram instâncias da API sem mudar código nem esquema, com p99 de 50 ms ou menos até 6 instâncias | `balance-read` e `mixed` do k6 em escala reduzida. O crescimento linear não foi medido |
| ESC-2 | A tabela de lançamentos cresce de 1 milhão para 1 bilhão de linhas, e chega um `asOf` de cinco anos atrás com cache frio | `asOf` e extrato mantêm o comportamento, com p99 de 50 ms ou menos no saldo e piora menor que 20% | `QueryPlanTests` confere o plano de consulta com 100.000 linhas. Volumes maiores não foram exercitados, e o bilhão não é testável localmente |
| ESC-3 | Sobe uma segunda instância do Worker | As duas dividem a publicação sem perder mensagem e com vazão de pelo menos 1,6 vez a de uma | `TwoPublishersTests` confere a divisão sem perda. A vazão de 1,6 vez não foi medida |
| TES-1 | Verificar o que acontece quando banco ou broker caem | Falha injetada por parada de contêiner ou por decorator de teste, e cada [cenário de falha](../08-resiliencia-e-operacao/cenarios-de-falha.md) aponta para um teste | Os testes F1 a F10 |
| MAN-1 | Trocar o RabbitMQ por outro broker | A mudança fica em `Ledger.Infrastructure` e na composição do Worker, sem alterar `Ledger.Domain` nem `Ledger.Application` | `LayerDependencyTests` |

Os nomes das métricas e a política de amostragem estão em [Catálogo de métricas](../08-resiliencia-e-operacao/catalogo-de-metricas.md).

## Leia também

- [Limites conhecidos](../09-qualidade/limites-conhecidos.md): o que não foi medido nem exercitado.
- [Capacidade e escala](../08-resiliencia-e-operacao/capacidade-e-escala.md): a conta de capacidade que sustenta as metas de dimensionamento.
- [Restrições e premissas](restricoes-e-premissas.md): as premissas por trás das metas.
