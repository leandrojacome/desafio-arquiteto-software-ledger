# Documento de arquitetura 0008: RabbitMQ com entrega ao menos uma vez

## Contexto

Os eventos de lançamento precisam chegar a sistemas que o ledger não controla, sem perda e sem que a gravação dependa do broker ([documento de arquitetura 0005](0005-transacao-unica-com-outbox.md)). O volume de referência é de 2.000 eventos por segundo no pico, uma premissa de dimensionamento. Exatamente uma vez de ponta a ponta não existe entre sistemas independentes. O que existe é entrega ao menos uma vez mais um consumidor que tolera repetição, e essa dupla produz o efeito de uma vez só.

## Decisão

O Worker lê a caixa de saída e publica no RabbitMQ 3 com publisher confirms e mensagens persistentes, numa exchange `topic` durável chamada `ledger.events`, com o tipo do evento como chave de roteamento (`EntryRegistered`). O ledger não declara fila de consumidor: cada um declara a sua e a liga à exchange. A publicação usa `mandatory`, e o Worker mantém uma fila de retenção própria, do tipo `quorum` e com exchange e fila de mensagens mortas, para que o broker nunca confirme e descarte uma mensagem sem destino ([documento de arquitetura 0033](0033-fila-de-retencao-e-publicacao-mandatory.md)).

Para as réplicas do Worker não pisarem umas nas outras, cada uma reivindica um lote numa transação curta: um `UPDATE` com `FOR UPDATE SKIP LOCKED` que marca um prazo (`locked_until`) e conta a tentativa. Depois publica o lote fora de transação, espera as confirmações e grava `published_at` nas confirmadas. Se o Worker morrer no meio, o prazo vence e outra réplica reivindica o lote, com possível duplicata, que o consumidor idempotente absorve. O algoritmo está em [Fluxo: publicação do outbox](../../06-fluxos/publicacao-do-outbox.md).

O `message_id` da mensagem é o identificador da linha do outbox e vale o mesmo que `eventId` no corpo. O consumidor deduplica por ele, gravando o id numa tabela de processados na mesma transação do seu efeito. A ordem não é garantida, então o evento leva `accountVersion` e `balanceAfter`, e o consumidor guarda a última versão aplicada por conta e ignora versões menores ou iguais. A publicação passa por um circuit breaker ([documento de arquitetura 0029](0029-publicador-do-outbox-com-circuito-por-mensagem.md)): com o broker fora, o Worker para de insistir e volta em modo de sonda.

A meta é p99 de até 5 segundos entre o commit e a publicação (NFR-13). Nenhum número foi medido sob carga ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md)).

## Alternativas descartadas

- Kafka. Melhor para reprocessamento e ordem por partição, e seria a escolha se o banco já tivesse uma plataforma de streaming. A 2.000 eventos por segundo e consumidores de fila de trabalho, é mais operação do que o problema pede. A porta de publicação fica na camada de aplicação, então trocar o adaptador é mudança local no Worker.
- Fila de nuvem (SQS, Service Bus). Prende a um provedor e piora o desenvolvimento local.
- Consumidores lendo o banco, por `LISTEN/NOTIFY` ou polling do outbox. Acopla cada um ao esquema do ledger e faz do banco de verdade um servidor de fila.
- Perseguir exatamente uma vez. Só vale dentro de um sistema, como nas transações do Kafka. Prometê-lo de ponta a ponta esconde a duplicata em vez de tratá-la.
- CDC com Debezium no lugar do polling. É boa evolução ([Evolução futura](../../11-evolucao/evolucao-futura.md)) e não muda o contrato com os consumidores.

## Consequências

Se o broker cair, as escritas continuam e os eventos esperam no outbox. Trocar de broker não mexe em domínio, API nem banco. O preço é que todo consumidor precisa ser idempotente ([Contrato de eventos](../../05-contratos/eventos.md)), mensagens podem chegar repetidas e fora de ordem, e o atraso mínimo é o do polling. O contador `attempts` serve a um alerta para a mensagem que o broker nunca aceita, por tamanho por exemplo, e que o Worker reivindica de novo a cada 30 segundos.
