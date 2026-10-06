# Idempotência e hash canônico

Uma requisição que estoura o tempo limite pode ter sido gravada ou não, e o cabeçalho `Idempotency-Key` permite repetir o pedido sem gravar o dinheiro duas vezes. A página define o formato da chave, o que a repetição devolve, como o ledger decide que dois pedidos são o mesmo e por quanto tempo a chave vale. O fluxo interno está em [repetição idempotente](../06-fluxos/repeticao-idempotente.md), e os códigos de erro no [Catálogo de erros](catalogo-de-erros.md).

## A chave

A chave é um texto de 1 a 128 caracteres ASCII visíveis, os bytes de `0x21` a `0x7E`. Espaço, caractere de controle e qualquer byte acima de `0x7E` são recusados, e o cabeçalho precisa aparecer exatamente uma vez.

| Rota | Chave | Escopo de unicidade | Tabela |
|---|---|---|---|
| `POST /v1/accounts/{accountId}/entries` | Obrigatória | A conta e a chave: a mesma chave em contas diferentes não colide | `idempotency_keys`, chave primária `(account_id, idempotency_key)` |
| `POST /v1/accounts/{accountId}/entries/{entryId}/reversals` | Obrigatória | A conta e a chave, no mesmo espaço dos lançamentos | `idempotency_keys` |
| `POST /v1/accounts` | Opcional | O chamador (claim `client_id` do token) e a chave | `account_creation_keys`, chave primária `(client_id, idempotency_key)` |

A validação responde 400 `VALIDATION_FAILED` com um item em `errors` cujo `field` é `Idempotency-Key`: `TOO_LONG` para mais de 128 caracteres e `INVALID_FORMAT` para cabeçalho repetido ou com caractere fora da faixa. Nos lançamentos e nos estornos, a falta do cabeçalho, ou um valor vazio, responde 400 `IDEMPOTENCY_KEY_REQUIRED`. Na criação de conta o cabeçalho é opcional, mas um valor vazio vira `INVALID_FORMAT`, porque quem envia o cabeçalho quer a proteção e um valor vazio é quase certamente defeito do chamador.

Qualquer texto que respeite a regra serve de chave. O melhor é derivá-la da intenção de negócio, para que uma nova tentativa da mesma operação reutilize o valor, ou gerar um UUID por operação e guardá-lo até a resposta chegar. Uma chave sequencial e curta (`1`, `2`, `3`) usada por mais de um sistema na mesma conta não corrompe nada, porque o `client_id` entra no hash, mas faz os dois se encontrarem num 422.

## O que a repetição devolve

A primeira requisição aceita grava o lançamento e consome a chave na mesma transação. Uma segunda, com a mesma chave e o mesmo conteúdo, devolve o status `201` e o corpo do lançamento original, lido do registro imutável e portanto idêntico ao da primeira resposta, o mesmo `Location`, o cabeçalho `Idempotent-Replayed: true` (ausente na primeira) e o `X-Correlation-Id` da nova chamada. A repetição não grava lançamento, não altera saldo e não gera evento, e na criação de conta não cria conta nem linha de auditoria.

A mesma chave com conteúdo diferente devolve 422 `IDEMPOTENCY_KEY_REUSED`, sem tocar em nada. Duas requisições simultâneas com a mesma chave produzem um lançamento só: a segunda espera a primeira terminar e cai no caminho de repetição.

Só o pedido gravado consome a chave. Uma recusa (saldo insuficiente, moeda diferente, lançamento inexistente, validação, limite de taxa) desfaz a transação inteira, inclusive a reserva da chave, e repetir o pedido depois de um crédito que cobre o débito é avaliado de novo e pode ser aceito. Um 503 não diz se o pedido foi gravado, e a resposta correta do chamador é repetir com a mesma chave e o mesmo corpo: o ledger devolve o lançamento se ele foi gravado e o grava se não foi.

### Exemplos

A repetição de um débito de R$ 80,00 enviado com a chave `debit-0001`. `Authorization` e cabeçalhos de segurança foram omitidos, como nos exemplos do [Contrato da API REST](api-rest.md).

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/entries
Idempotent-Replayed: true
X-Correlation-Id: 01a10d3c71f5734eb6df4545fd429678

{
  "entryId": "01a10d3c-71ec-7dd1-b27e-6667b3e58f28",
  "accountId": "01a10d3c-71ae-740a-b8d9-2fafe5818365",
  "accountVersion": 2,
  "type": "DEBIT",
  "amount": "80.00",
  "currency": "BRL",
  "balanceAfter": "920.00",
  "occurredAt": "2026-10-05T18:01:45.000000Z",
  "recordedAt": "2026-10-05T18:03:47.565454Z",
  "reversesEntryId": null,
  "description": "Pix enviado",
  "reference": "E18236120202610011403s0a1b2c3d4e"
}
```

A mesma chave, na mesma conta, com o mesmo corpo, enviada por outro chamador (`client_id` diferente), não é repetição: responde 422 `IDEMPOTENCY_KEY_REUSED`, e nada é gravado. A mesma chave com o valor trocado de R$ 80,00 para R$ 90,00 responde igual, sem dizer o que mudou. O corpo do erro está nos [exemplos do catálogo](catalogo-de-erros.md#exemplos).

Na criação de conta, a repetição com a mesma chave e o mesmo corpo devolve a conta já criada:

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /v1/accounts/01a10d3d-aeff-7d31-bdb9-f793895bf338/balance
Idempotent-Replayed: true
X-Correlation-Id: 01a10d3daf2e7c2db984f15734c71b27

{
  "accountId": "01a10d3d-aeff-7d31-bdb9-f793895bf338",
  "currency": "BRL",
  "overdraftLimit": "250.00",
  "holderDocumentMasked": "**.***.***/0001-**",
  "createdAt": "2026-10-05T18:05:08.736286Z"
}
```

A mesma chave com outro limite de cheque especial responde 422 `IDEMPOTENCY_KEY_REUSED` com o texto "A chave de idempotência já foi usada com um corpo de requisição diferente por este chamador.", porque aqui a chave pertence ao chamador.

## O hash canônico

O ledger compara o pedido atual com o guardado por um SHA-256, não pelos bytes do corpo: espaços, ordem das propriedades, `"80"` contra `"80.00"` ou o mesmo instante com outro fuso não podem transformar uma repetição legítima em 422. O `request_hash` é o SHA-256 de uma cadeia UTF-8 de campos separados por `\n`, sem separador no fim, e a coluna `hash_version` registra a regra com que a cadeia foi montada (hoje 1).

| Operação | Campos, na ordem |
|---|---|
| Registro de lançamento | `v1`, `entry.register`, `accountId`, `clientId`, `type`, `amount`, `currency`, `occurredAt`, `description`, `reference` |
| Estorno | `v1`, `entry.reverse`, `accountId`, `clientId`, `entryId`, `description` |
| Criação de conta | `v1`, `account.create`, `currency`, `overdraftLimit`, índice cego do documento |

Como cada campo é escrito:

| Campo | Forma na cadeia |
|---|---|
| `accountId`, `entryId` | Minúsculas, com hifens |
| `clientId` | A claim `client_id` do token, como veio |
| `type` | `CREDIT` ou `DEBIT` |
| `amount`, `overdraftLimit` | Decimal com duas casas e ponto, em cultura invariante |
| `occurredAt` | UTC no formato `yyyy-MM-ddTHH:mm:ss.ffffffZ`, depois de a API converter o instante recebido, qualquer que seja o deslocamento. Vazio quando o pedido não o trouxe |
| `description` | Texto aparado nas pontas. Vazio quando ausente, nulo ou só espaços |
| `reference` | Como veio. Vazio quando ausente |
| índice cego | Hexadecimal em minúsculas, 64 caracteres (HMAC-SHA-256 do documento normalizado) |

A `Idempotency-Key` não entra no hash, porque é a chave de busca. O `clientId` entra nos lançamentos e nos estornos porque a unicidade da chave é por conta, não por chamador: sem ele, dois sistemas com a mesma chave, a mesma conta e o mesmo corpo receberiam o mesmo lançamento, e o segundo acreditaria ter movido dinheiro que não moveu. Com ele, o encontro vira 422 ([documento de arquitetura 0023](../03-principios-e-decisoes/documento-arquitetura/0023-client-id-no-hash-do-pedido.md)). Na criação de conta o chamador já faz parte da chave primária e não entra no hash. A validação rejeita caracteres de controle nos textos livres, então um `\n` nunca aparece dentro de um campo e a concatenação é inequívoca.

### Equivalências e diferenças

Têm o mesmo hash, e portanto contam como o mesmo pedido: `amount` escrito como `"80"`, `"80.0"` ou `"80.00"`; `occurredAt` com qualquer deslocamento que dê o mesmo instante (`2026-10-01T11:03:10-03:00`, `2026-10-01T14:03:10Z`, `2026-10-01T14:03:10+00:00`, `2026-10-01T19:33:10+05:30`, `2026-10-01T14:03:10.000000Z` e `2026-10-01T14:03:10.0000000Z`, todos na cadeia como `2026-10-01T14:03:10.000000Z`); `description` com espaços ou tabulações nas pontas, ou ausente, vazia ou só de espaços; e chaves de idempotência diferentes, quando se compara só o cálculo.

Mudam o hash o `type`, o `amount`, a `currency`, o `occurredAt` (inclusive presente contra ausente), a `description`, a `reference`, a conta e o chamador. O mesmo horário de parede em outro fuso é outro instante: `2026-10-01T14:03:10-03:00` são 17:03:10 em UTC, e o hash difere do de `2026-10-01T14:03:10Z`. Um `occurredAt` sem fuso nem chega ao hash, porque a API o recusa antes (`MISSING_TIME_ZONE`). Um registro e um estorno nunca colidem, porque o segundo campo da cadeia identifica a operação.

### Criação de conta e o índice cego

O corpo da criação de conta contém o documento do titular, e um SHA-256 simples de um CPF se resolve por força bruta em minutos a partir de um despejo do banco. Por isso a cadeia leva o índice cego do documento, um HMAC com a chave de dados pessoais. Como o índice depende da versão da chave, a repetição calcula o hash com o índice de cada versão viva e aceita o pedido se algum casar: a rotação de chaves não transforma uma repetição legítima em 422 enquanto a versão usada na criação continuar viva. Uma versão aposentada antes de a chave expirar faz a repetição devolver 422, que falha para o lado seguro, porque nunca cria conta duplicada ([documento de arquitetura 0035](../03-principios-e-decisoes/documento-arquitetura/0035-idempotency-key-opcional-na-criacao-de-conta.md)). A proteção do documento está em [Proteção de dados](../07-consistencia-e-seguranca/protecao-de-dados.md).

## Vetores de teste

Os vetores vêm do `CanonicalRequestHashTests` (projeto `Ledger.Application.Tests`). A descrição faz parte do vetor, e outro texto produz outro hash, por isso a do estorno é fixa, mesmo em inglês. Todos usam a conta `0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33`, o chamador `pix-core` e, nos estornos, o lançamento original `0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10`.

| Caso | Entrada | SHA-256 em hexadecimal |
|---|---|---|
| Débito completo | `DEBIT`, `80.00`, `BRL`, `occurredAt` `2026-10-01T14:03:10Z`, descrição `Pix enviado`, referência `E18236120202610011403s0a1b2c3d4e` | `5fe1f2884e903b6e85b22f47e9b1feae60fac2ffeeecdf9f0e027b57a07da8be` |
| Crédito mínimo | `CREDIT`, `25.50`, `BRL`, sem `occurredAt`, sem descrição, sem referência | `c7ecef52b971b3d01150245f68f6131af4bc2e64decd779e01b9700c5e55a985` |
| Estorno com descrição | `Duplicate charge confirmed by reconciliation` | `62081fa4748137abd0a5e40df05c7fffea0f3bba8699e286ffd78402404aaedd` |
| Estorno sem descrição | Corpo vazio | `2126479df4ba5b4bf4a7260b4eccfd294db6a735c66e7054ccc2958063c73be0` |
| Débito de 90,00 | O primeiro vetor com `amount` `90.00` | `60dd6c42b73f1908980ee76b99e857f8ee23bf5329be030b114ce9cfa9892167` |

A cadeia do primeiro vetor, com uma linha por campo (o separador real é o caractere de quebra de linha):

```text
v1
entry.register
0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33
pix-core
DEBIT
80.00
BRL
2026-10-01T14:03:10.000000Z
Pix enviado
E18236120202610011403s0a1b2c3d4e
```

A criação de conta não tem vetor fixo, porque o índice cego depende de uma chave que muda por ambiente.

## Regras de comparação

O ledger reserva a chave com um `INSERT ... ON CONFLICT DO NOTHING` na mesma transação do lançamento, a partir da leitura da conta feita na mesma instrução. Se a conta não existe, a resposta é 404 `ACCOUNT_NOT_FOUND`, e se o `INSERT` grava a linha o pedido é novo. Com a conta existente e nenhuma linha gravada, a chave já foi usada: o ledger lê o registro guardado e recalcula o hash do pedido atual. Hash igual, comparado em tempo constante com `CryptographicOperations.FixedTimeEquals`, é repetição e devolve o lançamento original com `Idempotent-Replayed: true`. Hash diferente é 422 `IDEMPOTENCY_KEY_REUSED`. O passo a passo, com o SQL, está em [repetição idempotente](../06-fluxos/repeticao-idempotente.md).

Um registro com `hash_version` que o código não conhece é tratado como defeito e responde 500, porque o ledger não sabe recalcular a regra antiga. Hoje só existe a versão 1. Mudar a canonicalização exige uma `hash_version` nova, e a leitura da repetição passa a recalcular com a versão guardada em cada linha, durante os 35 dias da retenção.

## Retenção

A chave é guardada por 35 dias, contados de `created_at`, e depois o Worker a poda em lotes nas duas tabelas de chaves (`Idempotency:RetentionDays`). Depois da poda, o mesmo valor cria um lançamento novo, e por isso a janela precisa cobrir o maior prazo de reprocessamento dos chamadores. Essa cobertura é uma hipótese: os donos dos sistemas de Pix e de cartões não confirmaram que o maior prazo cabe nos 35 dias ([limites conhecidos](../09-qualidade/limites-conhecidos.md)). A decisão está no [documento de arquitetura 0017](../03-principios-e-decisoes/documento-arquitetura/0017-retencao-de-35-dias-das-chaves-de-idempotencia.md).
