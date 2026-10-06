# Cursor do extrato

O extrato é paginado por posição, não por deslocamento. A resposta de `GET /v1/accounts/{accountId}/entries` traz um `nextCursor`, que o chamador devolve em `cursor` para pedir a página seguinte. O cursor é opaco para quem o recebe e assinado pelo ledger. A rota está no [Contrato da API REST](api-rest.md), a leitura passo a passo em [extrato paginado](../06-fluxos/extrato.md) e a decisão no [documento de arquitetura 0025](../03-principios-e-decisoes/documento-arquitetura/0025-extrato-por-posicao-com-cursor-assinado.md).

## O que o chamador precisa saber

O chamador trata o cursor como texto sem estrutura: copia o `nextCursor` de uma resposta para o `cursor` do pedido seguinte e não interpreta o conteúdo. O formato pode mudar, e a versão dentro do cursor existe para isso. O `nextCursor` é `null` na última página, e enquanto vier um texto pode haver mais itens.

O cursor só vale para a conta que o gerou e guarda só a posição: `from`, `to` e `limit` vão de novo em cada pedido, e quem muda um filtro no meio da paginação obtém o novo filtro a partir da posição do cursor. Lançamentos novos entram sempre no topo do extrato e o cursor desce, então a página seguinte continua de onde a anterior parou, mesmo com escrita concorrente, sem repetir nem pular itens. Um cursor vive o tempo de uma paginação: trocar a chave de assinatura invalida os que estão em voo, e os chamadores recomeçam do início.

## Formato

O cursor é uma posição, o par `(recorded_at, account_version)` do último item da página, mais uma assinatura. São 33 bytes em base64url sem preenchimento, 44 caracteres do alfabeto `A-Z`, `a-z`, `0-9`, `-` e `_`.

| Bytes | Conteúdo |
|---|---|
| 1 | Versão do formato, hoje `0x01` |
| 8 | `recorded_at` do último item, em microssegundos desde 1970-01-01 UTC, inteiro de 64 bits com sinal, big-endian |
| 8 | `account_version` do último item, inteiro de 64 bits com sinal, big-endian, maior que zero |
| 16 | Assinatura: os 16 primeiros bytes do HMAC-SHA-256 do identificador da conta (16 bytes, na ordem do texto do UUID) seguido dos 17 bytes anteriores |

A chave do HMAC é `Security:Cursor:SigningKey`, 32 bytes em base64, separada das chaves de dados pessoais porque uma chave serve a uma finalidade só. A API não sobe sem ela, nem com uma chave que não decodifique para exatamente 32 bytes ([Configuração e linha de comando](configuracao.md)). O cursor não é segredo, porque a posição é do próprio chamador: a assinatura existe para o ledger não confiar numa posição fabricada ou levada de outra conta.

## Verificação

A API faz as verificações abaixo, nesta ordem lógica, e qualquer falha produz o mesmo resultado, sem dizer qual passo falhou.

1. O texto tem exatamente 44 caracteres, todos do alfabeto base64url, e decodifica para 33 bytes. O parâmetro aceita no máximo 64 caracteres, e um texto maior é recusado antes da decodificação. Padding `=`, o alfabeto padrão do base64 (`+` e `/`) e qualquer outro tamanho são recusados.
2. A assinatura recalculada com a conta da rota e a chave configurada coincide, em tempo constante, com os 16 bytes finais.
3. A versão do formato é `1`.
4. O `account_version` é maior que zero e o instante está no intervalo representável (anos 1 a 9999).

A recusa é o 400 `VALIDATION_FAILED` do [Catálogo de erros](catalogo-de-erros.md), com um item em `errors` cujo `field` é `cursor` e cuja razão é `INVALID_CURSOR`. O mesmo vale para o `cursor` repetido na consulta e para o `cursor` vazio.

## Relação com `from` e `to`

O limite superior do extrato é um só, e o cursor e o `to` o dizem de dois jeitos: "só o que vem antes de". O ledger junta os dois antes de consultar o banco e fica com o menor. Sem `to`, vale a posição do cursor. Sem cursor, `to` vale como a posição logo antes de todo lançamento registrado em `to`, o que mantém `to` exclusivo. O limite inferior `from` é inclusivo e não tem relação com o cursor.

## Vetores de teste

Os vetores vêm do `StatementCursorProtectorTests` (projeto `Ledger.Infrastructure.Tests`) e usam uma chave só de teste: os bytes de `0x01` a `0x20`, em base64 `AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=`. A conta é `0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33`.

| Caso | `recorded_at` | `account_version` | Cursor | Resultado |
|---|---|---|---|---|
| 1 | `2026-10-01T14:03:11.482913Z` | 1843 | `AQAGXMfgSmQhAAAAAAAABzPzGOCjl5YS7PXaNwBbM93l` | Aceito |
| 2 | `2026-10-01T14:20:45.118204Z` | 1851 | `AQAGXMgfF5b8AAAAAAAABzvZbkX-8qxg3DhgJH4ylC6Y` | Aceito |
| 3 | `2026-10-01T14:03:11.482913Z` | 1843 | `AQAGXMfgSmQhAAAAAAAABzOF7CDaVVMJMIpQZvl0weAS` | Recusado: assinado para a conta `0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d34` |
| 4 | `2026-10-01T14:03:11.482913Z` | 1843 | `AQAGXMfgSmQhAAAAAAAABzM9vzZISHD62Eplg0dm7r1C` | Recusado: assinado com a chave que vai de `0x02` a `0x21` |

O cursor do caso 1, decodificado, tem estes 33 bytes em hexadecimal:

```text
01 00065cc7e04a6421 0000000000000733 f318e0a3979612ecf5da37005b33dde5
|  |                 |                |
|  |                 |                assinatura (16 bytes)
|  |                 account_version = 1843
|  recorded_at = 1790863391482913 microssegundos
versão do formato
```
