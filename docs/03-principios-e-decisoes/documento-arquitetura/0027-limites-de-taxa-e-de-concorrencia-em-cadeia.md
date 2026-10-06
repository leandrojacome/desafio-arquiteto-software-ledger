# Documento de arquitetura 0027: Limites de taxa e de concorrência em cadeia

## Contexto

O [documento de arquitetura 0011](0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md) decidiu que a API limita a taxa por chamador e por conta e que leitura e escrita têm pools separados. Faltava dizer como os limites se combinam, em que ordem, o que cada recusa devolve e o que acontece com quem ainda não se identificou. A ordem importa porque cada limite gasta alguma coisa, uma ficha ou uma permissão de concorrência, e gastar o recurso de alguém por um pedido que o servidor nem atendeu é injusto.

## Decisão

Um limitador global encadeado, com seis políticas numa ordem fixa. Primeiro a concorrência da classe da requisição (`write-concurrency`, `balance-concurrency` e `statement-concurrency`, com 16, 16 e 8 permissões por padrão). A validação da configuração exige que cada uma fique entre uma e três vezes o pool de conexões da classe. Depois a cota do chamador, um balde de fichas por `client_id` e por tipo, leitura ou escrita (`write-per-client`, `read-per-client`). Por último a cota da conta, só para escrita (`write-per-account`), que protege a fila no lock de `account_balances`.

Cada endpoint declara a sua classe por metadado, e o `EndpointPolicyCoverageTests` confere que toda rota de negócio carrega a classe do contrato. O nome da política que recusou vira o rótulo `policy` da métrica `ledger.rate_limit.rejections` e não aparece na resposta. O limitador não enfileira: concede na hora ou recusa. A recusa por concorrência é 503 `SERVICE_UNAVAILABLE` com `Retry-After: 1`, porque a saturação é do servidor, e a recusa por cota é 429 `RATE_LIMITED` com o `Retry-After` do balde, no mínimo 1, porque o excesso é do chamador. O `Retry-After` não leva variação aleatória, para o teste e o chamador poderem contar com ele.

A cota por conta só existe quando o `accountId` da rota é um GUID válido: um identificador malformado não cria limitador e responde 404 sem tocar o banco. Quem não tem token é particionado pela origem do socket, e o `X-Forwarded-For` só vale quando o proxy está na lista de redes conhecidas, com um salto no máximo. A tabela de políticas, os padrões e as respostas estão em [Limites de taxa e de concorrência](../../08-resiliencia-e-operacao/limites-de-taxa-e-concorrencia.md).

## Alternativas descartadas

- Cota do chamador, depois a da conta e por fim a concorrência. A recusa por concorrência chegaria depois de as fichas terem sido gastas por um pedido que o servidor não atendeu. Com a concorrência na frente, quando ela recusa a cadeia para e nenhuma ficha é tocada, e a permissão tomada por um pedido que um elo posterior recusa volta sozinha.
- Uma política nomeada por rota (`RequireRateLimiting`). Aceita uma por rota e não compõe com a cota por chamador.
- Um middleware de limite próprio. Duplica o que o framework entrega, inclusive o ciclo de vida do lease.
- Uma partição comum para os identificadores inválidos. Um chamador com defeito receberia 429 no lugar do 404 que o ajudaria a achar o defeito.
- Limite global em Redis. Poria uma dependência nova no caminho de toda requisição.

## Consequências

O balde do chamador não devolve a ficha quando a conta recusa, então quem martela a mesma conta paga por isso, que é o efeito desejado. Os limites vivem na memória de cada instância, e com N instâncias o teto efetivo é N vezes o configurado, o que serve de proteção e não de cota contratual. Os padrões de concorrência saem do tamanho dos pools. As cotas padrão (balde de 3.000 fichas e reposição de 1.500 por segundo por escritor, 12.000 e 8.000 por leitor, 100 e 50 por conta) são valores iniciais, que não foram calibrados sob carga ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md)).
