# Documento de arquitetura 0033: Fila de retenção e publicação com `mandatory`

## Contexto

Numa exchange `topic`, a mensagem que nenhuma fila recebe é descartada pelo broker, que confirma a publicação do mesmo jeito. Se o Worker publica sem `mandatory`, grava `published_at` e o evento deixa de existir em qualquer lugar. No Compose, que não tem consumidor, o outbox marcaria todos os eventos como publicados enquanto a exchange `ledger.events` ficaria com zero filas e zero ligações.

A entrega ao menos uma vez ([documento de arquitetura 0008](0008-rabbitmq-entrega-ao-menos-uma-vez.md)) só vale depois de alguém declarar uma fila, e nada avisa que isso ainda não aconteceu. O requisito de que cada lançamento aceito gere um evento entregue (FR-19) não se cumpriria num ambiente sem consumidor.

## Decisão

Duas peças, que só funcionam juntas.

O Worker declara uma fila de retenção toda vez que conecta, ao lado da exchange: `retention.ledger.entry-registered`, do tipo `quorum`, durável e ligada com a chave `EntryRegistered`. Como a fila de qualquer consumidor, tem uma exchange de mensagens mortas e uma fila de descarte. Por ser rede de proteção e não arquivo, tem limites: 24 horas de TTL, 1.000.000 de mensagens e 1 GiB, com `x-overflow` igual a `drop-head`. O que expira ou é cortado vai para a fila de descarte, que guarda 7 dias e 100.000 mensagens. Os valores ficam em `RabbitMq:Retention`, e `Enabled=false` desliga a fila para quem já tem consumidores próprios. Se a fila existe com argumentos diferentes dos configurados, o Worker a mantém, liga a fila à exchange e avisa no log, em vez de ficar sem conectar.

A publicação usa `mandatory`. O broker devolve a mensagem que nenhuma fila recebeu, e o publicador a traduz no motivo `unroutable`. A mensagem devolvida não é marcada como publicada: volta ao outbox com a tentativa descontada, como no caso do broker indisponível, e a métrica `outbox.publish.failures` conta cada uma. O motivo não conta para o circuit breaker, porque o broker respondeu. O publicador marca a topologia como desatualizada e só reconecta depois do recuo exponencial da reconexão, de 1 a 30 segundos. Ao reconectar, declara tudo de novo, e é assim que uma fila apagada volta.

## Alternativas descartadas

- Publicar sem `mandatory` e documentar o limite. Num ambiente sem consumidor todo evento se perde sem aviso.
- Só `mandatory`. Evita a perda, mas no Compose o outbox ficaria parado e `outbox.oldest_pending.age` subiria para sempre, porque ninguém consome.
- Só a fila de retenção. Cobre o Compose e deixa a mesma janela aberta quando alguém apaga a fila ou quando um ambiente sobe sem consumidor e sem retenção.
- Um consumidor de referência no Compose. Mais uma peça para manter, quando o objetivo é só demonstrar a entrega ao broker, e a fila de retenção basta para isso.
- Fila sem limite, ou com `reject-publish`. Sem consumidor, a fila sem limite encheria o disco do broker, e com `reject-publish` o broker passaria a recusar também a publicação destinada às filas dos consumidores de verdade. O `drop-head` perde as mensagens mais antigas, que o extrato reconstrói.

## Consequências

Cada evento tem onde esperar. A fila de retenção não é fonte da verdade: no ritmo médio premissado de 200 eventos por segundo, 1.000.000 de mensagens cobrem cerca de 83 minutos, e no pico premissado de 2.000, cerca de 8. Quem precisa de mais declara a própria fila. Esses volumes são premissas ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md)).

A conta do Worker no broker precisa de permissão de configuração sobre essas filas e exchanges, e mudar o limite de uma fila já declarada não tem efeito sem apagá-la. Se a recriação automática da fila atrapalhar um consumidor real, ou se o volume real mostrar que 1.000.000 de mensagens não cobrem o tempo que um consumidor leva para entrar no ar, a retenção terá de mudar.
