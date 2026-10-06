# Capacidade e escala

Esta página faz a conta de quanto o ledger precisa crescer: lançamentos por dia, espaço em disco, conexões com o banco, quanto uma única conta aguenta e quanto o outbox acumula quando o broker cai. Todo número de volume é premissa minha, nenhum veio do banco que vai usar o sistema, e nenhuma carga em escala foi executada. A página dimensiona e não garante capacidade. O que foi verificado em escala reduzida e o que ficou sem medida está em [Limites conhecidos](../09-qualidade/limites-conhecidos.md). As metas vêm dos [requisitos não funcionais](../02-contexto-e-requisitos/requisitos-nao-funcionais.md), e as políticas que as sustentam, de [Políticas de resiliência](politicas-de-resiliencia.md) e [Limites de taxa e de concorrência](limites-de-taxa-e-concorrencia.md).

## Premissas de dimensionamento

| Item | Valor | Origem |
|---|---|---|
| Pico de escrita no total | 2.000 lançamentos por segundo | Meta NFR-01 |
| Pico de escrita na mesma conta | 50 por segundo | Meta NFR-01 |
| Pico de leitura de saldo | 10.000 consultas por segundo | Meta NFR-02 |
| Retenção dos lançamentos | 10 anos, sem remoção física | Premissa de negócio |
| Média anualizada de escrita | 200 lançamentos por segundo, um décimo do pico | Premissa |
| Contas ativas | 20 milhões | Premissa |
| Extrato | 500 consultas por segundo no pico | Premissa |
| Linha de `ledger_entries` | 430 bytes: cerca de 200 de heap e 230 de índices | Estimativa, com texto livre de tamanho real |
| Duração de uma transação de escrita, do `BEGIN` ao commit | 6 ms, com commit síncrono no mesmo datacenter | Estimativa sem medição |
| Duração de uma consulta de saldo no banco | 1,5 ms | Estimativa sem medição |
| Mensagem no outbox | 500 bytes | Estimativa para o envelope e o payload do [evento](../05-contratos/eventos.md) |
| Linha de `idempotency_keys` | Cerca de 150 bytes, com a chave primária | Estimativa |
| Vazão do publicador | 3.000 mensagens por segundo por instância do Worker | Estimativa sem medição |
| Retenção das chaves de idempotência | 35 dias | [documento de arquitetura 0017](../03-principios-e-decisoes/documento-arquitetura/0017-retencao-de-35-dias-das-chaves-de-idempotencia.md), em `Idempotency:RetentionDays` |

A média de um décimo do pico é a premissa que mais pesa no volume em disco. O pico de 2.000 por segundo é um evento raro (dia de pagamento, campanha), e a média de um ano inclui madrugadas e domingos. Das durações, a mais sensível é a do commit, porque ela decide quanto uma conta quente aguenta (seção "A linha da conta") e só se mede na topologia real.

## A conta

Os resultados seguem das premissas e mudam na mesma proporção se a média real de escrita for outra.

| Grandeza | Conta | Resultado |
|---|---|---|
| Lançamentos por dia | 200 por segundo vezes 86.400 | 17,3 milhões |
| Lançamentos por ano | 17,28 milhões vezes 365 | 6,3 bilhões |
| Lançamentos em 10 anos | 6,3 bilhões vezes 10 | 63 bilhões |
| Lançamentos por conta ativa por ano | 6,3 bilhões divididos por 20 milhões | Cerca de 315 |
| Crescimento de `ledger_entries` por dia | 17,28 milhões vezes 430 bytes | Cerca de 7,4 GB |
| Crescimento por ano | 6,3 bilhões vezes 430 bytes | Cerca de 2,7 TB |
| Volume em 10 anos | 2,7 TB vezes 10 | Cerca de 27 TB |
| Partição mensal média | 526 milhões de linhas vezes 430 bytes | Cerca de 226 GB |
| `account_balances` | 20 milhões de linhas, com `fillfactor` de 70 e o índice | Cerca de 3,5 GB, o que cabe na memória (estimativa) |
| WAL gerado | Cerca de 1 KB por lançamento (estimativa) | 18 GB por dia na média, 2 MB por segundo no pico (7,4 GB por hora) |
| Caixa de saída | 500 bytes por mensagem | 360 MB por hora na média, 3,6 GB por hora no pico, e cerca de 60 GB em regime com 7 dias de retenção das publicadas |
| Chaves de idempotência com a poda de 35 dias | 17,28 milhões por dia vezes 35 dias vezes 150 bytes | 605 milhões de linhas, cerca de 90 GB |
| A mesma tabela se as chaves nunca expirassem | 6,3 bilhões por ano vezes 150 bytes | Cerca de 950 GB por ano, 9,5 TB em 10 anos |

A soma mostra por que as chaves têm prazo: sem poda, `ledger_entries` e `idempotency_keys` somariam cerca de 3,7 TB por ano, e a segunda tabela, que só protege contra reenvio, seria 26% do total.

A taxa de escrita não é o problema. Duas mil transações por segundo, de quatro comandos cada, cabem com folga num primário de 16 vCPU com standby síncrono: estimo uns 2 núcleos para a escrita e 1 para a leitura de saldo, o que deixa mais de quatro vezes de folga para as escritas e as 10.000 leituras por segundo. É hipótese, sem medição. O problema é o volume: 27 TB numa tabela só são difíceis de operar. O gatilho de 300 GB para particionar chega em cerca de 40 dias nesse ritmo (300 GB divididos por 7,4 GB por dia), e o de um bilhão de linhas em 58. Por isso o particionamento mensal é pré-requisito de um lançamento em escala, se o volume real confirmar a premissa, e não evolução opcional ([Evolução futura](../11-evolucao/evolucao-futura.md)).

| Média de escrita | Volume em 10 anos | Dias até 300 GB |
|---|---|---|
| 100 por segundo (metade da premissa) | Cerca de 13,6 TB | Cerca de 81 |
| 200 por segundo (premissa) | Cerca de 27 TB | Cerca de 40 |
| 600 por segundo (o triplo) | Cerca de 81 TB | Cerca de 13 |

A arquitetura sobrevive aos três casos. O que muda é o calendário: com o triplo da média, o particionamento passa a exigir camadas de armazenamento.

## Conexões com o banco

O orçamento de conexões sai da lei de Little: as conexões ocupadas em média são a taxa de chegada vezes o tempo de ocupação. Dimensiono com folga de três vezes, para absorver variação, e divido pelo máximo de 6 instâncias da API.

| Consumidor | Taxa de pico | Tempo na conexão | Ocupadas em média | Com folga de 3 | Por instância (6) | Pool configurado |
|---|---|---|---|---|---|---|
| Escrita | 2.000 por segundo | 6 ms | 12 | 36 | 6 | 7 (total 42) |
| Saldo | 10.000 por segundo | 1,5 ms | 15 | 45 | 7,5 | 8 (total 48) |
| Extrato | 500 por segundo | 10 ms | 5 | 15 | 2,5 | 4 (total 24) |
| Worker | | | | | | 5 por instância, 2 instâncias (total 10) |
| Reserva (migração, administração, replicação) | | | | | | 20 |

A soma é de 144 conexões contra `max_connections` de 200: 72% de ocupação e folga de 56 (28%). Cada instância da API fica com no máximo 19 conexões (pools de 7, 8 e 4), e os pools configurados ficam um pouco acima do calculado, principalmente o do extrato. A regra para qualquer mudança é que instâncias vezes a soma dos pools, mais o Worker, mais a reserva, caibam em `max_connections`. A aplicação não consegue verificar isso, porque não conhece as outras instâncias, e quem acompanha é a medição de conexões abertas contra `max_connections` no banco. Os limites de concorrência de cada instância (16, 16 e 8) derivam desse orçamento, entre uma e três vezes o pool de cada fonte, porque um limite maior que o pool só empurra a espera para dentro do pool, onde ela custa mais. Subir o pool de escrita de 7 para 12 por instância leva o total de 144 para 174, ainda dentro de 200.

## A linha da conta

O gargalo de um ledger costuma ser uma linha, não o banco inteiro. A de `account_balances` serializa todas as escritas de uma conta e fica travada do `UPDATE` ao commit. Com 6 ms nesse intervalo, o teto é de cerca de 166 lançamentos por segundo numa conta (1 dividido por 0,006). Para ver o que acontece antes do teto, uso a fila M/M/1 como estimativa pessimista, com tempo de serviço exponencial, e calculo a espera que só 1% das requisições ultrapassa:

| Taxa na conta | Ocupação da linha | Espera p99 na fila |
|---|---|---|
| 50 por segundo (premissa) | 30% | Cerca de 29 ms |
| 80 por segundo | 48% | Cerca de 45 ms |
| 100 por segundo | 60% | Cerca de 61 ms |
| 120 por segundo | 72% | Cerca de 92 ms |
| 130 por segundo | 78% | Cerca de 119 ms |
| 150 por segundo | 90% | Cerca de 270 ms |

A espera p99 de 29 ms a 50 por segundo cabe com sobra na meta de 150 ms. O p99 da escrita cruza 150 ms perto de 130 a 140 lançamentos por segundo, somando a espera, os 6 ms de serviço e o resto da requisição, e a partir de uns 165 por segundo a fila cresce sem limite e o `lock_timeout` de 1 s passa a produzir `503` com `Retry-After`. O M/M/1 é pessimista, porque o tempo de serviço real varia menos que o exponencial. Mesmo assim, o limite por conta é uma premissa a acordar com os chamadores e não um detalhe de implementação.

O teto depende do tempo que a linha fica travada, e esse tempo inclui o commit. Com standby síncrono, o commit espera a confirmação dele:

| Acréscimo de latência no commit | Tempo com a linha travada | Teto por conta |
|---|---|---|
| Nenhum | 6 ms | Cerca de 166 por segundo |
| 20 ms | 26 ms | Cerca de 38 por segundo |
| 50 ms | 56 ms | Cerca de 18 por segundo |

Uma lentidão de replicação que passa despercebida no p99 geral derrubaria primeiro as contas mais quentes, abaixo dos 50 por segundo da premissa. É conta, não medida: o ambiente local não tem standby síncrono para produzir esse atraso.

## Estratégia de escala

O crescimento segue uma ordem, do mais barato para o mais caro, e cada degrau tem o gatilho que o justifica em [Evolução futura](../11-evolucao/evolucao-futura.md).

A API cresce para os lados: não guarda estado, não precisa de afinidade de sessão e valida o token localmente, com as chaves do emissor em cache. Planejo 4 instâncias no pico, o que permite perder uma sem saturar o pool de escrita, e o teto de 6 vem do orçamento de conexões, não da CPU. Estimo que uma instância de 2 vCPU absorva de 2.000 a 3.000 requisições por segundo e que o pico combinado de uns 12.000 por segundo caiba em 4 a 6, também sem medição. Para passar de 6, o caminho é reduzir o pool por instância ou pôr um PgBouncer em modo de transação na frente, revendo antes os comandos preparados automáticos do Npgsql, que não convivem bem com esse modo.

O banco cresce para cima primeiro, porque existe um único primário de escrita: mais CPU, mais memória e disco mais rápido, sem mudar código. As migrações já criam `account_balances` com `fillfactor` baixo e autovacuum agressivo, para atualizar no lugar, e `ledger_entries` com a coleta de inserções ajustada, para manter o mapa de visibilidade em dia, sem o qual a busca por índice do saldo em um instante vira leitura de heap. Em produção, recomendo o WAL em disco separado. Os degraus seguintes são o microlote por conta, o particionamento mensal de `ledger_entries` por `recorded_at`, a réplica de leitura para extrato e `asOf` antigos, o cache de saldo e, por último, a divisão por conta em vários bancos, com a regra de que o saldo atual nunca sai de réplica nem de cache, porque é com ele que alguém decide se pode debitar.

## Acúmulo do outbox

Com o broker fora, o outbox acumula na taxa de escrita, e a drenagem depende da vazão do publicador, que estimo em 3.000 mensagens por segundo por instância do Worker, sem medição. O tempo de drenagem é o acúmulo dividido pela vazão líquida, que é a do publicador menos a taxa de lançamentos novos.

| Cenário | Acúmulo | Tamanho | Drenagem com 1 instância | Drenagem com 2 instâncias |
|---|---|---|---|---|
| Uma hora de broker fora na média (200 por segundo) | 720 mil mensagens | 360 MB | Cerca de 4,3 minutos | Cerca de 2,1 minutos |
| Uma hora de broker fora no pico (2.000 por segundo) | 7,2 milhões de mensagens | 3,6 GB | Cerca de 2 horas | Cerca de 30 minutos |

A meta de esvaziar o acúmulo da hora média em até 5 minutos depois da volta, com 2 instâncias, é cumprida pela conta. O alerta de idade da mensagem pendente mais antiga dispara muito antes de qualquer consumidor reclamar. O risco que sobra é o broker fora por muitas horas num pico, que enche o disco do banco ([cenário 13](cenarios-de-falha-9-a-16.md)). A poda remove só as publicadas, depois de 7 dias (`Outbox:RetentionDays`), em lotes de 5.000 (`Outbox:PruneBatchSize`).

## O que a conta não mede

Os cenários do k6 rodam por padrão em um décimo da escala, na mesma máquina da pilha: mostram que as rotas respondem e que os invariantes se mantêm sob carga, não a capacidade. O teto por conta depende sobretudo do custo do commit, que só se mede com rede, `fsync` e standby síncrono. O `QueryPlanTests` confere que o saldo em um instante é um `Index Only Scan` e que o extrato anda no mesmo índice sem ordenar, mas numa tabela de cem mil lançamentos, o que não substitui a medição com um bilhão de linhas. O que falta medir e como eu mediria está em [Limites conhecidos](../09-qualidade/limites-conhecidos.md).

## Onde a conta aperta

O primeiro risco real do desenho é a conta quente. Um sistema que lance a contrapartida de todo Pix numa conta única da instituição chega ao teto da linha muito antes dos 2.000 por segundo totais, e a saída mais barata é pedir aos chamadores que espalhem contas internas muito quentes em várias. Já o saldo atual cabe na memória e a consulta `asOf` é uma busca por índice, o que sustenta a decisão de não ter cache de saldo ([documento de arquitetura 0009](../03-principios-e-decisoes/documento-arquitetura/0009-sem-cache-na-v1.md)).
