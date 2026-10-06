# Documento de arquitetura 0030: Prazo próprio para marcar e liberar as mensagens do lote

## Contexto

O Worker reivindica um lote do outbox, publica cada mensagem, espera as confirmações e marca como publicadas as que foram confirmadas. Entre a confirmação do broker e a marcação no banco há uma janela em que o broker já tem a mensagem e o ledger ainda não sabe. Se a marcação falha ou nem chega a rodar, a reivindicação vence e a mensagem sai de novo. O [documento de arquitetura 0008](0008-rabbitmq-entrega-ao-menos-uma-vez.md) aceita a duplicata, porque o consumidor deduplica pelo identificador do evento, mas há duas situações em que a duplicata em massa seria evitável. Uma é o prazo da publicação ter estourado: o lote tem 5 segundos para confirmar, e se parte das mensagens confirmou perto do fim, o prazo já está gasto quando chega a hora de marcar. A outra é o desligamento, em que o `SIGTERM` cancela o token do serviço no meio de um lote e as mensagens já confirmadas ficariam sem marca.

## Decisão

A marcação das confirmadas e a devolução das não tentadas rodam num bloco `finally`, cada uma com um prazo só dela: um `CancellationTokenSource` de `Outbox:LeaseSeconds` (30 segundos por padrão), criado na hora, sem relação com o prazo de confirmação nem com o token do serviço. A configuração só vale se a reivindicação durar mais que a confirmação, e `OutboxSettings` recusa o contrário. Assim a marcação sempre tem tempo de rodar.

São duas instruções em lote, uma por grupo de mensagens. `MarkOutboxPublishedSql` grava `published_at` e zera `locked_until` dos confirmados. `ReleaseOutboxSql` zera `locked_until` e devolve a tentativa (`attempts - 1`) das mensagens que nem chegaram a ser aceitas, porque o broker estava indisponível, o prazo estourou ou nenhuma fila recebeu a mensagem. É por isso que uma indisponibilidade do broker, por mais longa que seja, não gasta tentativa de ninguém e não aciona o alerta de mensagem venenosa. O texto das duas instruções está em [Fluxo: publicação do outbox](../../06-fluxos/publicacao-do-outbox.md).

## Alternativas descartadas

- Marcar sob o prazo da confirmação. Funciona no caminho feliz e falha exatamente quando o lote demorou, que é quando a marcação mais importa.
- Marcar sob o token do serviço. No desligamento ele já está cancelado, a marcação aborta na hora e o lote confirmado volta a ser publicado na próxima reivindicação.
- Marcar sem prazo nenhum. Um banco travado seguraria o desligamento além do `ShutdownTimeout` do host, que também é de 30 segundos.
- Marcar cada mensagem logo depois da confirmação. São até 200 idas ao banco por lote contra uma só com `id = ANY(@ids)`, e a janela de duplicata só diminui, não some.
- Não devolver a tentativa das mensagens não aceitas. Uma indisponibilidade longa empurraria mensagens saudáveis para o limiar do alerta de mensagem venenosa.

## Consequências

O desligamento termina o lote em curso, publicado e marcado, e não reivindica outro, caminho coberto pelo `PublisherShutdownTests`. Se o banco estiver fora por mais que o prazo, a marcação falha, o Worker registra e as mensagens voltam quando a reivindicação vencer. Nesse caso a duplicata é inevitável e o consumidor a descarta. O custo é um segundo `CancellationTokenSource` por lote e uma regra de configuração a mais, e quem calibrar os prazos precisa saber que o da marcação é o da reivindicação. Quantas mensagens saem duplicadas por desligamento não foi medido sob carga ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md)), e os prazos devem ser revistos se esse número passar a importar.
