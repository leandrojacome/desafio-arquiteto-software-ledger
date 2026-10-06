# Riscos e alternativas rejeitadas

A primeira parte explica por que o desenho é como é, alternativa por alternativa, e quando valeria reconsiderar. A segunda lista o que pode dar errado, com a mitigação que já existe, o reforço que ainda seria possível e o sinal que avisa. O que não foi medido está em [Limites conhecidos](../09-qualidade/limites-conhecidos.md), e as decisões em si, no [índice de documentos de arquitetura](documento-arquitetura/README.md).

## Alternativas rejeitadas

### Event sourcing completo

O ledger já só insere, então a vantagem principal do event sourcing, a história auditável, vem de graça. O resto seria replay, snapshots, projeções eventualmente consistentes e o custo de evoluir o esquema de eventos para sempre, cerimônia demais para crédito, débito e estorno. O saldo em `account_balances` e em `balance_after` é estado derivado atualizado atomicamente, não uma projeção que pode atrasar. Vale reconsiderar se aparecerem vários agregados com regras temporais complexas ([documento de arquitetura 0002](documento-arquitetura/0002-ledger-imutavel-somente-insercao.md), [documento de arquitetura 0003](documento-arquitetura/0003-saldo-apos-em-cada-lancamento.md)).

### CQRS com projeção

Um modelo de leitura assíncrono daria saldo atrasado, justamente o que uma decisão de débito não pode ler, e a leitura já é uma busca por índice nos mesmos dados. Leitura e escrita já são separadas na lógica (caminhos, pools e portas distintos), sem projeção. Para o que o ledger não atende bem, como busca por texto na descrição ou agregação por período, uma projeção alimentada pelos eventos passa a fazer sentido, só para isso.

### Saga

Uma saga coordena transações entre serviços ou bancos, e aqui um lançamento é uma transação local num banco só. A transferência entre contas é feita pelo chamador, com dois lançamentos independentes e estorno como compensação, e se virar função do ledger continuará local. A saga só volta se as contas passarem a morar em bancos diferentes ([documento de arquitetura 0005](documento-arquitetura/0005-transacao-unica-com-outbox.md)).

### EF Core

O que importa no sistema é o SQL de concorrência: o `UPDATE` condicional com `RETURNING`, a reivindicação de lote com `FOR UPDATE SKIP LOCKED`, as comparações entre vizinhos da conferência. O EF Core obrigaria a escrevê-lo como SQL cru, e o resto (rastreamento de mudanças, unidade de trabalho) não serve a inserções imutáveis. As migrações em SQL puro com DbUp ficam legíveis para quem revisa banco, ao custo de escrever à mão o mapeamento do cadastro de contas e algumas consultas simples ([documento de arquitetura 0007](documento-arquitetura/0007-postgresql-npgsql-dapper-dbup.md)).

### Kafka

Faria sentido com muitos consumidores independentes que reprocessam histórico, ou com ordenação garantida por conta de ponta a ponta, e nenhum dos dois existe. Dois mil eventos por segundo no pico premissado é pouco para o RabbitMQ, e a fonte para reprocessar é o próprio ledger. O custo operacional do Kafka (cluster, partições, retenção, registro de esquemas) não se paga, e como só o publicador fala com o broker, trocar depois é contido ([documento de arquitetura 0008](documento-arquitetura/0008-rabbitmq-entrega-ao-menos-uma-vez.md)).

### Redis

Um segundo lugar com o saldo traria invalidação, leitura defasada e avalanche quando a chave expira, para resolver uma latência que o banco, pela estimativa, já entrega (cerca de 1,5 ms por consulta, sem medida sob carga). Também ficou de fora do limite de taxa, e a consequência, limite local por instância, está nos riscos abaixo ([documento de arquitetura 0009](documento-arquitetura/0009-sem-cache-na-v1.md), [documento de arquitetura 0011](documento-arquitetura/0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md)).

### Microsserviços

O invariante central, saldo, lançamento e mensagem de saída numa transação só, desaparece se esses pedaços forem separados por rede. Dividir também multiplicaria o que se opera (deploy, observabilidade, contratos, versões) sem aliviar gargalo algum, porque o gargalo, quando existir, é uma linha de banco. O único corte com justificativa operacional é o do Worker ([documento de arquitetura 0001](documento-arquitetura/0001-monolito-modular-dois-executaveis.md)).

### Alternativas menores

| Alternativa | Por que não | Quando reconsiderar |
|---|---|---|
| Saldo calculado por soma a cada consulta | O custo cresce com o histórico da conta | Nunca como caminho principal |
| Ler com `SELECT ... FOR UPDATE`, calcular na aplicação e gravar | Uma ida e volta a mais e lock por mais tempo, sem ganho de segurança sobre o `UPDATE` condicional | Nunca |
| Aceitar a escrita numa fila e responder `202` | O chamador precisa saber na hora se houve saldo, e uma fila na frente esconde falha | Se um chamador específico aceitar resposta assíncrona |
| Saldo bitemporal (por `occurred_at`) | Lançamento retroativo reescreveria a cadeia de `balance_after`, que é imutável | Exigência contábil ou regulatória ([documento de arquitetura 0013](documento-arquitetura/0013-tempo-do-saldo-registrado-em-vs-ocorrido-em.md)) |
| Snapshots periódicos de saldo | O `balance_after` já resolve a consulta por instante | Fechamento diário em lote lento |

## Riscos de negócio

O custo de uma duplicidade, de uma perda ou de um saldo errado está em [Contexto de negócio](../02-contexto-e-requisitos/contexto-de-negocio.md). A tabela diz o que o projeto faz a respeito.

| Risco | O que acontece com o negócio | Resposta do projeto | Teste |
|---|---|---|---|
| Reenvio de um chamador gera lançamento duplicado | Cliente debitado duas vezes, ou banco creditando duas vezes | BR-11 a BR-13, [documento de arquitetura 0006](documento-arquitetura/0006-idempotencia-por-chave-e-hash.md) | Integração com a mesma chave em paralelo |
| Corrida entre débitos simultâneos fura o saldo | Exposição de crédito não autorizada | BR-06, BR-10, [documento de arquitetura 0004](documento-arquitetura/0004-saldo-corrente-com-update-condicional.md) | `ParallelDebitsTests`, `ParallelDebitsHttpTests` |
| Falha no meio da operação deixa saldo e extrato em desacordo | Conciliação com diferença e auditoria sem resposta | BR-14, BR-17, [documento de arquitetura 0005](documento-arquitetura/0005-transacao-unica-com-outbox.md) | Testes de falha com banco real |
| Perda de lançamento confirmado em falha de infraestrutura | Dinheiro que deixa de existir no registro | RPO zero com standby síncrono, topologia só descrita ([Implantação em produção](../04-modelos-c4/implantacao-producao.md)) | Nenhum ensaio de failover ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)) |
| Pico acima do dimensionado | Lentidão, timeouts, reenvios em cascata | Limites de taxa e 503 com `Retry-After` ([documento de arquitetura 0027](documento-arquitetura/0027-limites-de-taxa-e-de-concorrencia-em-cadeia.md)) | Integração. Sob carga acima do perfil, não medido |
| Vazamento de documento ou de saldo | Multa da LGPD, violação de sigilo bancário, dano à imagem | BR-19 a BR-21, [Segurança](../07-consistencia-e-seguranca/seguranca.md) | Testes de autorização e de ausência de dado pessoal no log |
| Metas de dimensionamento erradas | Sistema subdimensionado e instável, ou superdimensionado e caro | Medir a linha de base, testar carga e revisar as metas | Ainda não medido ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)) |
| Crescimento da tabela de lançamentos em dez anos | Custo de armazenamento e degradação lenta | Particionamento mensal, fora da primeira versão ([Evolução futura](../11-evolucao/evolucao-futura.md)) | Volume real ainda não medido |

## Riscos de arquitetura

Na coluna Mitigação, "existe" é o que está no código ou na configuração. "Reforço possível" é só ideia: nada foi implementado nem exercitado.

| Risco | Efeito | Mitigação | Sinal de alerta |
|---|---|---|---|
| Conta de contrapartida da instituição concentra o tráfego | Teto estimado de cerca de 166 lançamentos por segundo na conta (estimativa, sem medida), fila e 503 | Existe: `lock_timeout` de 1 segundo, limite por conta e 503 com `Retry-After`. Reforço possível: acordo com os chamadores para espalhar contas internas, balanceamento por `accountId` e microlote por conta | Espera por lock acima de 30 ms numa conta; 503 por `lock_timeout` |
| Tabela de lançamentos cresce sem particionar | Vacuum, índices e backup ficam lentos; no volume premissado passa de 300 GB em cerca de 40 dias | Existe: autovacuum de inserção configurado na migração `0001`. Reforço possível: particionamento mensal antes do lançamento em escala | Tamanho da tabela; duração do vacuum |
| Bloat em `account_balances` | A linha atualizada a cada lançamento gera versões mortas, e a tabela incha | Existe: `fillfactor` 70, `autovacuum_vacuum_scale_factor` de 0,02 e nenhuma coluna atualizada indexada, para os updates serem HOT | Tuplas mortas; proporção de HOT updates; tamanho da tabela contra 3,5 GB estimados |
| Caixa de saída cresce sem parar | Disco enche, polling fica lento | Existe: poda das publicadas em 7 dias, métrica de idade da mais antiga, circuit breaker no publicador e fila de retenção com limites | `outbox.oldest_pending.age` |
| Falha do standby síncrono trava a escrita | Com um único standby síncrono, commits esperam até alguém agir | Reforço possível: commit síncrono por quórum com dois standbys, e nunca rebaixar para assíncrono automaticamente | Atraso de flush na replicação; latência de commit |
| Relógio do primário salta para trás, por exemplo num failover para nó atrasado | `recorded_at` fora de ordem na conta, `asOf` incoerente | Existe: o `UPDATE` impõe `recorded_at` estritamente crescente por conta ([documento de arquitetura 0018](documento-arquitetura/0018-monotonicidade-do-recorded-at-e-janela-de-acomodacao.md)) e a conferência acusa regressão. O salto real do relógio num failover não foi exercitado | `ledger.recorded_at.corrections`; verificação `CHAIN_NON_MONOTONIC` |
| Perda ou comprometimento da chave de dados pessoais | Documentos ilegíveis ou expostos | Existe: versões de chave, recifragem em lote e caminho do dinheiro independente da chave. Reforço possível: cofre externo e custódia dividida | Falhas de acesso ao cofre; evento `pii.decrypted` na trilha |
| Alteração manual no banco por quem tem privilégio | Cadeia de saldos quebrada, sem rastro | Existe: papéis sem `UPDATE` e `DELETE` em `ledger_entries`, gatilho que recusa e conferência contínua. Reforço possível: `pgaudit` e checksums de dados no PostgreSQL de produção, que o repositório não configura | Tentativa barrada pelo gatilho; divergência achada pela conferência |
| RTO de 15 minutos não cobre corrupção lógica | Restaurar um banco de vários terabytes leva horas, e a promoção de um standby, que só foi descrita, nunca medida, não resolve problema de dados | O RTO vale para perda de nó ou de zona. Corrupção se trata com conferência, correção por novo lançamento e, em último caso, restauração pontual no tempo, ainda não exercitada ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)) | Tempo de restauração medido em ensaio |
| Consumidores não deduplicam | Efeitos duplicados fora do ledger (notificação ou contagem em dobro) | Existe: contrato com o identificador do evento e um consumidor de referência que só existe nos testes. Não há consumidor real | Reclamação de duplicidade; taxa de reentrega |
| Um erro de integridade não bloqueia a conta | A conta suspeita continua recebendo lançamentos até alguém agir | Existe: detecção em até 5 minutos e alerta de página. Reforço possível: situação de quarentena na conta ([Questões em aberto](questoes-em-aberto.md)) | `ledger.integrity.violations` acima de zero |
| Limites de taxa locais a cada instância | Com N instâncias o teto efetivo é N vezes o configurado, e a cota por conta de 50 por segundo pode ser superada | Existe: limite de concorrência e `lock_timeout` protegem o banco. Reforço possível: balanceamento por `accountId` ou cota global em gateway | Rejeições por política; espera por lock |
| Escopo `ledger.write` permite descobrir saldos | Um chamador comprometido lê saldo por tentativas de débito | Existe: `client_id` em tudo, limite de taxa por cliente, tokens curtos e a trilha ([documento de arquitetura 0010](documento-arquitetura/0010-seguranca-jwt-e-criptografia-de-pii.md)). Reforço possível: alertas por cliente sobre `INSUFFICIENT_FUNDS` e estornos | Taxa de `INSUFFICIENT_FUNDS` e de estornos por `client_id` |
| Prazos do banco sem validação cruzada na subida | Uma configuração incoerente (comando menor que o bloqueio, por exemplo) é aceita sem aviso | Existe: padrões coerentes entre si (bloqueio de 1 s, comando de 2 s, instrução de 2,5 s, requisição de 3 s). Não existe validador da relação entre eles | Estouros de timeout fora do padrão |
| Os pipelines nunca executaram nos serviços e podem esbarrar em limites deles | A primeira execução pode falhar na expansão dos modelos, na aprovação do envio das imagens, por memória ou pelo limite de downloads do Docker Hub | Existe: `CiPipelineTests` e `GitHubWorkflowTests` conferem a estrutura dos arquivos. Reforço possível: pool maior, autenticação no Docker Hub ou espelho das imagens | Falha na primeira execução |

## Leia também

- [Princípios de arquitetura](principios-de-arquitetura.md): o que orienta as escolhas entre atributos de qualidade.
- [Cenários de falha](../08-resiliencia-e-operacao/cenarios-de-falha.md): o que o sistema faz em cada falha.
- [Capacidade e escala](../08-resiliencia-e-operacao/capacidade-e-escala.md): a conta de capacidade por trás dos riscos de volume.
