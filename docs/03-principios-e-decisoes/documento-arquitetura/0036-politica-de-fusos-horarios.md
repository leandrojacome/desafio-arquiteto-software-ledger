# Documento de arquitetura 0036: Política de fusos horários

## Contexto

Os chamadores do ledger são sistemas de uma empresa brasileira, e o horário que eles conhecem é o de Brasília, UTC-3 o ano inteiro desde que o Brasil abandonou o horário de verão, em 2019. O ledger guarda e calcula em UTC: o saldo em um instante, o `recorded_at`, o cursor do extrato e o hash do pedido usam o mesmo relógio.

A entrada precisa ser igualmente consistente. Se `occurredAt` aceita qualquer deslocamento mas `asOf`, `from` e `to` só aceitam `Z`, quem quer o extrato do dia 10 de março em Brasília converte os limites à mão, e quem esquece o deslocamento recebe uma mensagem genérica de formato inválido, sem saber que faltou o fuso.

## Decisão

`occurredAt`, `asOf`, `from` e `to` aceitam `Z` ou um deslocamento `±HH:MM` de `-14:00` a `+14:00`, com até seis casas de fração. A fração pode ter de sete a nove casas se os dígitos depois da sexta forem zero, para que o formato `o` do .NET e os nanossegundos de outras linguagens passem sem perder precisão. Um dígito diferente de zero ali é recusado, porque o ledger guarda microssegundos e não arredonda. O leitor converte o instante para UTC antes de qualquer outro uso, e dali em diante comando, hash canônico, SQL, cursor, evento e auditoria só veem UTC: `2026-10-01T11:03:10-03:00` e `2026-10-01T14:03:10Z` são o mesmo instante em todo o sistema.

O instante sem deslocamento é recusado. Em `occurredAt`, `from` e `to` a resposta é 400 com a razão `MISSING_TIME_ZONE`, e o `asOf` responde 400 `INVALID_AS_OF`, com mensagem que pede o fuso. Os demais defeitos de forma (deslocamento fora da faixa, `+0300`, data inexistente) são `INVALID_FORMAT`. O deslocamento `-00:00` também é recusado: na RFC 3339 ele significa "fuso local desconhecido", o contrário do que a regra pede.

O `occurredAt` tem um piso, `1970-01-01T00:00:00Z`, medido em UTC depois da conversão, e um instante anterior responde 400 `OUT_OF_RANGE`. O motivo é o `default(DateTimeOffset)` de um cliente .NET: o Npgsql converte `DateTime.MinValue` em `-infinity`, e a data de negócio de um lançamento imutável ficaria gravada como sentinela. O piso é 1970 e não 1900 porque relaxar uma validação é compatível e apertá-la não é. Como defesa em profundidade, a API e o Worker ligam `Npgsql.DisableDateTimeInfinityConversions`. O `asOf`, o `from` e o `to` não têm piso.

A saída é sempre em UTC: todo instante sai como `yyyy-MM-ddTHH:mm:ss.ffffffZ`, qualquer que seja o deslocamento da entrada. O hash também depende só do instante, porque a cadeia canônica escreve UTC: o mesmo instante com deslocamentos diferentes produz o mesmo hash, e uma repetição com outro deslocamento é uma repetição.

## Alternativas descartadas

- Saída em `-03:00`. Dividiria o ledger em duas representações, a API em `-03:00` e os eventos, o cursor e o hash em UTC, e textos com `Z` ordenam alfabeticamente enquanto textos com deslocamentos misturados não. Quem quer a hora local converte com uma subtração.
- Fuso configurável por chamador. O mesmo corpo passaria a significar coisas diferentes conforme quem o envia, o hash dependeria de um cadastro que pode mudar e o ledger teria de carregar as regras históricas de horário de verão. Nenhum requisito paga esse custo.
- Instante local sem deslocamento, assumindo Brasília quando faltar. Um chamador em UTC que esquece o `Z` gravaria um instante errado em três horas, e num ledger imutável isso só se corrige com estorno. Recusar com uma mensagem que orienta custa uma chamada a mais e nunca grava errado.
- Aceitar só UTC. Obriga o chamador a converter os limites do dia de Brasília e não separa "faltou o fuso" de "o formato está errado".

## Consequências

Quem chama de Brasília pede o dia civil com `from=2026-03-10T00:00:00-03:00` e `to=2026-03-11T00:00:00-03:00`. Esse dia e o dia UTC da mesma data não coincidem, já que um lançamento às 23:30 em Brasília cai no dia seguinte em UTC, e a API não adivinha qual dos dois o chamador quer. Ela também não confere se o deslocamento condiz com o fuso real do chamador: o instante vale pelo que foi escrito.

A razão `MISSING_TIME_ZONE` é estável e consta no [Catálogo de erros](../../05-contratos/catalogo-de-erros.md), e os testes estão em `tests/Ledger.Api.IntegrationTests/TimeZones`. Se aparecer um chamador de outro país que precise de resposta em horário local, o caminho é um parâmetro de apresentação, sem mexer no armazenamento. Se o Brasil retomar o horário de verão, os exemplos com `-03:00` deixam de valer o ano inteiro, e a API segue igual, porque só lê o deslocamento escrito.
