# Contrato de eventos

Todo lançamento aceito, inclusive o estorno, gera um evento `EntryRegistered`. O Worker o publica no RabbitMQ, e os sistemas que reagem a lançamentos (conciliação, extrato próprio, notificação) o consomem. A página diz o que o ledger garante e o que o consumidor precisa cumprir. O caminho da gravação à publicação está em [publicação do outbox](../06-fluxos/publicacao-do-outbox.md), e o broker fora do ar em [falha do broker](../06-fluxos/falha-do-broker.md).

## O que o ledger promete

| Garantia | Detalhe |
|---|---|
| Entrega pelo menos uma vez | Nenhum evento confirmado no banco se perde. Uma mensagem pode chegar mais de uma vez |
| Identidade estável | O `message_id` da mensagem, o `eventId` do corpo e o `id` da linha do outbox são o mesmo valor, em toda republicação |
| Numeração sem lacuna por conta | O `accountVersion` de uma conta vai de 1 a N sem buraco. Se chegar a 1845 depois da 1843, falta a 1844 |
| Conteúdo sem dado pessoal | O evento não traz descrição, referência nem documento do titular |
| Versão do esquema | `schemaVersion` 1 no corpo e no cabeçalho `schema-version` |

O ledger não promete entrega exatamente uma vez, nem ordem entre eventos (nem dentro de uma conta), nem que a fila do consumidor exista quando o evento sai, nem reentrega depois de sete dias, porque a caixa de saída é podada. A criação de conta não gera evento.

O atraso entre o commit e a publicação ficar abaixo de 5 segundos no p99, na escala das premissas, é uma meta que ainda não foi medida sob carga ([limites conhecidos](../09-qualidade/limites-conhecidos.md)).

## O evento `EntryRegistered`

O corpo é um objeto JSON com 14 propriedades. Instantes saem em UTC no formato `yyyy-MM-ddTHH:mm:ss.ffffffZ`, com seis casas, e valores monetários como texto decimal com duas casas.

| Propriedade | Tipo | Anulável | Significado |
|---|---|---|---|
| `eventId` | texto (UUID) | não | Identificador do evento. É o `message_id` da mensagem e o `id` da linha do outbox |
| `eventType` | texto | não | Sempre `EntryRegistered` |
| `schemaVersion` | inteiro | não | Versão do esquema do corpo. Hoje 1 |
| `accountId` | texto (UUID) | não | Conta do lançamento |
| `entryId` | texto (UUID) | não | Lançamento |
| `accountVersion` | inteiro | não | Posição do lançamento na conta, a partir de 1 |
| `type` | texto | não | `CREDIT` ou `DEBIT` |
| `amount` | texto decimal | não | Valor, sempre positivo |
| `currency` | texto | não | Moeda do lançamento, ISO 4217 |
| `balanceAfter` | texto decimal | não | Saldo da conta logo depois do lançamento |
| `recordedAt` | texto (instante) | não | Instante do registro, pelo relógio do banco |
| `occurredAt` | texto (instante) | não | Data de negócio informada pelo chamador, ou o `recordedAt` quando ele não informou |
| `reversesEntryId` | texto (UUID) | sim | Lançamento original, quando o evento é de um estorno. Caso contrário, `null` |
| `correlationId` | texto | não | O `X-Correlation-Id` da requisição que gerou o lançamento |

O `balanceAfter` é o saldo logo depois do lançamento, não o atual, e quem precisa do saldo em um instante usa o último até ele. O corpo é o texto do `jsonb` guardado em `outbox_messages.payload`, que o PostgreSQL normaliza: as propriedades saem ordenadas por tamanho do nome e depois em ordem alfabética, com um espaço depois de `:` e de `,`. A ordem das propriedades não faz parte do contrato. Este é o corpo de um débito de R$ 80,00 com `occurredAt` enviado em outro fuso:

```json
{
  "type": "DEBIT",
  "amount": "80.00",
  "entryId": "01a10c61-bcef-7c42-93a8-6e5b8924c77c",
  "eventId": "01a10c61-bcef-743b-a326-03562a5dbb36",
  "currency": "BRL",
  "accountId": "01a10c61-b763-78ab-ba84-62e5e72bf602",
  "eventType": "EntryRegistered",
  "occurredAt": "2026-10-05T14:02:54.000000Z",
  "recordedAt": "2026-10-05T14:04:54.390724Z",
  "balanceAfter": "920.00",
  "correlationId": "01a10c61bce774f6b4c7fe574b1965d2",
  "schemaVersion": 1,
  "accountVersion": 2,
  "reversesEntryId": null
}
```

## O envelope AMQP

O envelope é a própria linha do outbox, e o Worker a transforma em mensagem sem abrir o JSON do corpo.

| Coluna do outbox | Na mensagem |
|---|---|
| `id` | Propriedade `message_id`, em minúsculas e com hifens. O consumidor deduplica por ele |
| `type` | Propriedade `type` e chave de roteamento |
| `payload` | Corpo UTF-8, com `content_type` `application/json` e `content_encoding` `utf-8` |
| `correlation_id` | Propriedade `correlation_id` |
| `traceparent` | Cabeçalho `traceparent`, só quando a linha tem um. Liga o consumo ao trace da requisição original |
| `created_at` | Propriedade `timestamp`, em segundos desde 1970 |
| `account_id` | Cabeçalho `account-id` |
| Constante | `delivery_mode` 2 (persistente) e cabeçalho `schema-version` com o valor 1 |

O cabeçalho `x-dotnet-pub-seq-no`, acrescentado pela biblioteca cliente, é do transporte e o consumidor o ignora. A mesma mensagem, com as propriedades e os cabeçalhos como a API de gerenciamento do broker os mostra:

```json
{
  "exchange": "ledger.events",
  "routing_key": "EntryRegistered",
  "properties": {
    "type": "EntryRegistered",
    "timestamp": 1791209094,
    "message_id": "01a10c61-bcef-743b-a326-03562a5dbb36",
    "correlation_id": "01a10c61bce774f6b4c7fe574b1965d2",
    "delivery_mode": 2,
    "headers": {
      "account-id": "01a10c61-b763-78ab-ba84-62e5e72bf602",
      "schema-version": 1,
      "traceparent": "00-827da5833ffe61c1a6daf0e4c4458164-be1b4f219d3a43d3-01",
      "x-dotnet-pub-seq-no": 2
    },
    "content_encoding": "utf-8",
    "content_type": "application/json"
  }
}
```

## Topologia no broker

O Worker declara a topologia, de forma idempotente, a cada conexão nova. O ledger não declara nenhuma fila de consumidor.

| Item | Valor |
|---|---|
| Troca | `ledger.events`, tipo `topic`, durável, sem exclusão automática |
| Chave de roteamento | O `type` do evento. Hoje, `EntryRegistered` |
| Publicação | Com confirmação do publicador e `mandatory` verdadeiro |
| Fila de retenção | `retention.ledger.entry-registered`, `quorum`, durável, ligada à troca com a chave `EntryRegistered` |
| Argumentos da fila de retenção | `x-message-ttl` de 24 horas, `x-max-length` de 1.000.000, `x-max-length-bytes` de 1 GiB, `x-overflow` igual a `drop-head`, e mensagens cortadas vão para a troca de mortas com a chave `dead` |
| Troca de mortas | `retention.ledger.entry-registered.dlx`, tipo `direct`, durável |
| Fila de mortas | `retention.ledger.entry-registered.dead-letter`, `quorum`, durável, ligada à troca de mortas com a chave `dead`, com `x-message-ttl` de 7 dias e `x-max-length` de 100.000 |
| Filas dos consumidores | Dos consumidores. Cada uma se chama `<consumidor>.ledger.entry-registered` |

Os valores da fila de retenção vêm de `RabbitMq:Retention` ([Configuração e linha de comando](configuracao.md)), e com `Enabled` igual a `false` o Worker declara só a troca. Se a fila já existe com argumentos diferentes dos configurados, o broker recusa a redeclaração com o erro 406: o Worker a mantém como está, liga a fila à troca e registra o evento de log 3010, em vez de ficar sem conectar. Mudar um limite de uma fila existente exige apagá-la e deixar o Worker declará-la de novo. A conta do Worker no broker precisa de permissão de configuração sobre essas filas e trocas, além de escrita e leitura.

A publicação é `mandatory` porque, sem isso, o broker descartaria a mensagem publicada sem nenhuma fila ligada, confirmaria mesmo assim, e o ledger a marcaria como publicada. Com `mandatory`, o broker a devolve, o publicador a classifica como não roteável e o ledger não a dá por publicada ([documento de arquitetura 0033](../03-principios-e-decisoes/documento-arquitetura/0033-fila-de-retencao-e-publicacao-mandatory.md)). A fila de retenção é uma rede de proteção, não a fila de um consumidor: tempo e tamanho limitam o que fica guardado, e o que passou disso se reconstrói pelo extrato.

Os argumentos das duas filas, como a API de gerenciamento do broker os lista:

```json
{
  "retention.ledger.entry-registered": {
    "x-dead-letter-exchange": "retention.ledger.entry-registered.dlx",
    "x-dead-letter-routing-key": "dead",
    "x-max-length": 1000000,
    "x-max-length-bytes": 1073741824,
    "x-message-ttl": 86400000,
    "x-overflow": "drop-head",
    "x-queue-type": "quorum"
  },
  "retention.ledger.entry-registered.dead-letter": {
    "x-max-length": 100000,
    "x-message-ttl": 604800000,
    "x-overflow": "drop-head",
    "x-queue-type": "quorum"
  }
}
```

## O que o consumidor deve fazer

1. Declarar a própria fila antes de precisar do evento: `quorum`, durável, com o nome `<consumidor>.ledger.entry-registered`, ligada à troca `ledger.events` com a chave `EntryRegistered` e com uma troca e uma fila de mortas próprias. O que a fila de retenção já descartou por tempo ou tamanho só o extrato devolve.
2. Deduplicar pelo `message_id`, gravando-o numa tabela de eventos processados na mesma transação do efeito, para o efeito e o registro de que ele aconteceu nunca se separarem:

   ```sql
   INSERT INTO processed_events (message_id, processed_at)
   VALUES (@message_id, clock_timestamp())
   ON CONFLICT (message_id) DO NOTHING;
   ```

   Uma linha afetada é evento novo, e o efeito segue na mesma transação. Zero linhas é duplicata: confirma a mensagem e não repete o efeito. A tabela é do consumidor, e o ledger não a cria.
3. Confirmar a mensagem ao broker só depois do commit. Um consumidor que cai entre o commit e a confirmação recebe a mensagem de novo e a reconhece como duplicata.
4. Não depender da ordem. Guardar o último `accountVersion` aplicado por conta e comparar com o do evento recebido:
   - menor ou igual ao último: evento velho, ignorar;
   - igual ao último mais um: o esperado;
   - maior que o último mais um: faltam eventos. Aplicar o efeito se ele for por estado (`balanceAfter`), registrar a lacuna e recuperar as versões que faltam pelo extrato.
5. Ignorar a propriedade que não conhece, e recusar sem reenfileirar, para a fila de mortas, o evento cujo `schemaVersion` não suporta.
6. Rejeitar sem reenfileirar o que nunca vai poder processar, em vez de reentregar para sempre, e acompanhar a profundidade da fila de mortas.
7. Registrar o `message_id` e o `correlation_id` no log, nunca o corpo.

## Reconstruir o que se perdeu

Uma lacuna de versão, uma fila que não existia ou um consumidor novo que precisa do histórico se reconstroem pelo extrato (`GET /v1/accounts/{accountId}/entries`), a fonte de verdade: cada item traz `accountVersion` e `balanceAfter`. Dentro dos sete dias da caixa de saída, quem opera também pode republicar uma janela de eventos, e a deduplicação pelo `message_id` torna isso seguro.

## Compatibilidade e versionamento

Propriedade nova no corpo mantém o `schemaVersion`, e por isso o consumidor ignora o que não conhece. Uma mudança incompatível cria outro tipo de evento (`EntryRegisteredV2`), publicado em paralelo com o antigo durante a transição, com a própria chave de roteamento. O cabeçalho `schema-version` e a propriedade `schemaVersion` do corpo sempre coincidem.

## Consumidor de referência

Os testes de integração têm um consumidor de referência, em `tests/Ledger.Api.IntegrationTests/Infrastructure/ReferenceConsumer.cs`, que faz o que as regras acima mandam. Não é código de produção, e as tabelas `consumer_ref.*` que ele usa são criadas pelo teste num esquema do banco descartável, nunca por migração do ledger.
