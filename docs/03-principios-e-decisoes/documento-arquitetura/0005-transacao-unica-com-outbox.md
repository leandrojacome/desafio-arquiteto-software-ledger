# Documento de arquitetura 0005: Transação única com outbox

## Contexto

Todo lançamento confirmado precisa virar evento para quem vive fora do ledger: extrato, conciliação, antifraude, notificações. O problema clássico é a escrita dupla. Gravar no banco e depois publicar perde o evento se o processo morrer entre os passos. Publicar primeiro deixa um evento sobre um lançamento que não existe se o commit falhar. Nenhuma das duas serve, e o broker fora do ar também não pode derrubar a gravação (meta de disponibilidade de 99,95%, NFR-05).

## Decisão

Tudo o que precisa ser atômico acontece numa única transação do PostgreSQL, nesta ordem: reserva da chave em `idempotency_keys` ([documento de arquitetura 0006](0006-idempotencia-por-chave-e-hash.md)); `UPDATE` condicional em `account_balances` e `INSERT` do lançamento com o `balance_after` devolvido, na mesma instrução ([documento de arquitetura 0004](0004-saldo-corrente-com-update-condicional.md)); `INSERT` em `outbox_messages` com o evento em `jsonb`; `COMMIT`.

A requisição HTTP nunca fala com o RabbitMQ. O evento nasce como linha da caixa de saída, no mesmo commit do lançamento, e o Worker o leva ao broker ([documento de arquitetura 0008](0008-rabbitmq-entrega-ao-menos-uma-vez.md)). O identificador da linha é o do evento, e é por ele que os consumidores deduplicam. O payload nunca carrega dado pessoal ([Contrato de eventos](../../05-contratos/eventos.md)). A transação não faz chamada de rede além do próprio PostgreSQL: validação e hash do pedido acontecem antes do `BEGIN`.

| Onde o processo cai | O que existe no banco | Como se resolve |
|---|---|---|
| Antes do commit | Nada, por rollback | O chamador repete com a mesma `Idempotency-Key` |
| Depois do commit, antes da resposta | Lançamento e evento | O chamador repete e recebe a resposta original |
| Depois do commit, antes da publicação | Lançamento e evento pendente | O Worker publica na próxima volta |

## Alternativas descartadas

- Publicar depois do commit, na própria requisição. Perde o evento se o processo morrer entre os passos, e a falha só aparece semanas depois, numa conciliação que não fecha.
- Publicar antes do commit. Gera evento fantasma sobre lançamento revertido, e em ledger evento falso é pior que evento atrasado.
- Transação distribuída entre PostgreSQL e RabbitMQ. O RabbitMQ não participa de XA.
- Confirmação síncrona do broker dentro da requisição. Acopla a disponibilidade da escrita à do broker, que é o que se quer evitar.
- CDC lendo o WAL, com Debezium. É a opção mais elegante em escala maior ([Evolução futura](../../11-evolucao/evolucao-futura.md)), mas pediria mais infraestrutura do que um polling de poucas centenas de milissegundos justifica a 2.000 eventos por segundo.

## Consequências

Atomicidade real, e uma falha de broker invisível para quem escreve: os cenários de queda ([Cenários de falha](../../08-resiliencia-e-operacao/cenarios-de-falha.md)) viram "eventos acumulam e saem depois", não "escritas falham".

O preço é uma escrita a mais por requisição e eventos que chegam com atraso igual ao intervalo de polling mais o tempo de publicação (a meta e o que ainda não foi medido estão no [documento de arquitetura 0008](0008-rabbitmq-entrega-ao-menos-uma-vez.md)). Com o broker fora a caixa de saída cresce, uns 3,6 GB por hora de pico nas premissas, e só muitas horas seguidas nesse ritmo enchem o disco do banco ([Capacidade e escala](../../08-resiliencia-e-operacao/capacidade-e-escala.md)). O Worker remove em lotes as linhas publicadas há mais de sete dias, o que não fere o [documento de arquitetura 0002](0002-ledger-imutavel-somente-insercao.md), porque o outbox é fila de integração e não registro contábil.
