# Catálogo de erros

Toda resposta de erro da API é um documento Problem Details (RFC 9457) com um código estável. O chamador decide pelo `code` e pelo status HTTP, nunca pelo texto. A página lista os códigos, o texto exato de cada mensagem e as razões de validação. As rotas estão no [Contrato da API REST](api-rest.md).

## Formato

Todo erro sai com `Content-Type: application/problem+json; charset=utf-8` e as propriedades abaixo. A ordem delas no JSON pode variar entre respostas, e o chamador não deve depender dela.

| Propriedade | Tipo | Significado |
|---|---|---|
| `type` | texto (URI) | Identificador estável do tipo de erro, derivado do `code`: o prefixo `https://ledger.bank.internal/problems/` seguido do código em minúsculas e com hifens (`insufficient-funds`). O prefixo é só um identificador, e o endereço não precisa resolver |
| `title` | texto | Resumo curto, fixo para cada código, em português e sem ponto final |
| `status` | inteiro | Status HTTP da resposta |
| `detail` | texto | Explicação em português, numa frase terminada em ponto. Pode mudar entre versões e não deve ser interpretada por código. Nunca repete um valor recebido, só o nome do campo e a regra |
| `instance` | texto | Caminho da requisição |
| `code` | texto | O identificador estável do catálogo, em `SCREAMING_SNAKE_CASE` e em inglês. É a parte do contrato em que o chamador deve se apoiar |
| `correlationId` | texto | O `X-Correlation-Id` da resposta, para citar em um chamado |
| `traceId` | texto | Identificador do trace da requisição, que liga o erro aos traces e aos logs |
| `errors` | lista | Só no `VALIDATION_FAILED` de campos inválidos. Um item por problema, com `field`, `reason` e `message` |

Em cada item de `errors`, `field` é o nome da propriedade do JSON, do parâmetro de consulta ou do cabeçalho (ou `$` para o corpo inteiro), `reason` é um código estável, listado adiante, e `message` é uma frase fixa em português que nomeia o campo e a regra. A validação acumula todos os problemas de um pedido numa lista só.

`title`, `detail` e `message` estão em português e usam o vocabulário do [Glossário](../01-visao-geral/glossario.md). `code`, `type`, `field`, `reason`, os nomes de campo, de parâmetro e de cabeçalho (entre aspas simples dentro das frases) e os tokens de protocolo continuam em inglês ([documento de arquitetura 0037](../03-principios-e-decisoes/documento-arquitetura/0037-idioma-das-mensagens-ao-chamador.md)). A codificação do corpo, com os caracteres de HTML escapados, está no [Contrato da API REST](api-rest.md), e os exemplos desta página mostram o texto já decodificado.

## Como o chamador deve tratar

| Status | O que significa | O que fazer |
|---|---|---|
| 400 | O pedido está mal formado | Corrigir o pedido. Repetir igual não adianta |
| 401 | Falta autenticação válida | Obter um token novo e repetir |
| 403 | O token não dá acesso a esta rota | Não repetir. É problema de permissão do chamador |
| 404 | A conta, o lançamento ou a rota não existe | Conferir o identificador. A resposta é a mesma para um identificador inexistente e para um mal formado |
| 409 | O estado atual conflita com o pedido | Ler o estado, sem repetir o pedido às cegas |
| 413, 415 | O corpo é grande demais ou não é JSON | Corrigir o pedido |
| 422 | O pedido está bem formado, mas o negócio não o aceita | Tratar como resposta de negócio. A recusa não consome a chave, então um `INSUFFICIENT_FUNDS` pode virar aceito se a conta receber dinheiro e o mesmo pedido for repetido |
| 429 | A cota foi excedida | Esperar o `Retry-After` e repetir com a mesma chave |
| 503 | Uma dependência não respondeu, ou a capacidade está saturada | Esperar o `Retry-After` e repetir com a mesma chave e o mesmo corpo. O pedido pode ou não ter sido gravado, e a repetição é segura |
| 500 | Falha inesperada | Citar o `correlationId` ao reportar. Repetir com a mesma chave é seguro nas rotas de escrita |

Um código que o chamador não conhece deve cair num ramo padrão pelo status.

## Catálogo

| Status | `code` | `title` | Quando ocorre | Rotas |
|---|---|---|---|---|
| 400 | `VALIDATION_FAILED` | Falha na validação da requisição | Propriedade ou parâmetro ausente, mal formado, fora dos limites, desconhecido ou repetido. Instante sem fuso horário. Cabeçalho `Idempotency-Key` inválido. Cursor inválido. `from` não anterior a `to`. Corpo que não é um objeto JSON | Todas |
| 400 | `IDEMPOTENCY_KEY_REQUIRED` | Cabeçalho 'Idempotency-Key' obrigatório | Lançamento ou estorno sem o cabeçalho, ou com valor vazio, depois de o corpo estar válido | `POST` de lançamento e de estorno |
| 400 | `INVALID_AS_OF` | Instante 'asOf' inválido | `asOf` sem fuso horário, mal formado (as regras estão em [Datas e fusos horários](api-rest.md#datas-e-fusos-horários)), repetido ou posterior ao relógio do banco | `GET` do saldo |
| 401 | `UNAUTHENTICATED` | Autenticação necessária | Token ausente, inválido, vencido, de outro emissor ou de outra audiência. O `WWW-Authenticate` não diz o motivo | Todas as de negócio |
| 403 | `FORBIDDEN` | Acesso negado | Token válido sem o escopo da rota, sem a claim `client_id`, com `client_id` fora das regras ou, na criação de conta, de um chamador fora da lista de provisionamento | Todas as de negócio |
| 404 | `ACCOUNT_NOT_FOUND` | Conta não encontrada | A conta não existe, ou o identificador não é um UUID de 36 caracteres com hifens | As rotas de uma conta |
| 404 | `ENTRY_NOT_FOUND` | Lançamento não encontrado | O lançamento não existe nesta conta, ou o identificador não é um UUID válido | `POST` de estorno |
| 404 | `NOT_FOUND` | Recurso não encontrado | Rota inexistente, com um token válido. Sem token, a mesma rota responde 401 | Qualquer caminho |
| 405 | `METHOD_NOT_ALLOWED` | Método não permitido | Método não suportado na rota. O `Allow` lista os aceitos | Qualquer rota |
| 409 | `ENTRY_ALREADY_REVERSED` | Lançamento já estornado | O lançamento já tem estorno, inclusive quando dois estornos concorrem | `POST` de estorno |
| 413 | `PAYLOAD_TOO_LARGE` | Corpo da requisição grande demais | Corpo com mais de 16 KiB (16384 bytes) | Os `POST` |
| 415 | `UNSUPPORTED_MEDIA_TYPE` | Tipo de mídia não suportado | `Content-Type` diferente de `application/json`, `charset` diferente de `utf-8`, ou ausente. O estorno com corpo vazio dispensa o cabeçalho | Os `POST` |
| 422 | `INSUFFICIENT_FUNDS` | Saldo insuficiente | O saldo resultante ficaria abaixo de menos o limite da conta, num débito ou no estorno de um crédito já gasto. Não informa o saldo e não grava nada | `POST` de lançamento e de estorno |
| 422 | `CURRENCY_MISMATCH` | Moeda diferente da moeda da conta | A moeda do pedido difere da moeda da conta | `POST` de lançamento |
| 422 | `IDEMPOTENCY_KEY_REUSED` | Chave de idempotência reutilizada em outra requisição | A mesma chave com conteúdo diferente, ou usada por outro chamador na mesma conta. Nada é gravado | `POST` de lançamento, de estorno e de conta (com chave) |
| 422 | `ENTRY_NOT_REVERSIBLE` | Lançamento não pode ser estornado | O lançamento é ele mesmo um estorno | `POST` de estorno |
| 429 | `RATE_LIMITED` | Limite de requisições excedido | A cota do chamador ou da conta foi excedida. Traz `Retry-After` | Todas as de negócio |
| 500 | `INTERNAL_ERROR` | Erro interno | Falha inesperada. Não traz detalhe interno, só o `correlationId` | Todas |
| 503 | `SERVICE_UNAVAILABLE` | Serviço temporariamente indisponível | Dependência indisponível, tempo da requisição esgotado, `lock_timeout`, pool de conexões ou limite de concorrência saturados. Traz `Retry-After` | Todas |

`ENTRY_NOT_REVERSIBLE` vale antes de `ENTRY_ALREADY_REVERSED`: o estorno de um estorno responde 422 mesmo quando o estorno original já foi estornado. `ACCOUNT_NOT_FOUND` e `ENTRY_NOT_FOUND` são uniformes: o corpo não repete o identificador pedido (só o `instance` o traz) nem revela se a conta existe com outra grafia.

### Texto do detalhe

O `detail` padrão de cada código aparece em toda resposta que nasce do pipeline (401, 403, 404 de rota, 405, 413 pelo limite do servidor, 429, 500 e 503) e na validação de campos.

| `code` | `detail` |
|---|---|
| `VALIDATION_FAILED` | Um ou mais campos são inválidos. |
| `IDEMPOTENCY_KEY_REQUIRED` | O cabeçalho 'Idempotency-Key' é obrigatório para esta requisição. |
| `INVALID_AS_OF` | O parâmetro 'asOf' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', ter no máximo 6 casas decimais de segundo e não estar no futuro. |
| `UNAUTHENTICATED` | É necessária autenticação para acessar este recurso. |
| `FORBIDDEN` | O token não concede acesso a este recurso. |
| `ACCOUNT_NOT_FOUND` | A conta não existe. |
| `ENTRY_NOT_FOUND` | O lançamento não existe nesta conta. |
| `ENTRY_ALREADY_REVERSED` | O lançamento já foi estornado. |
| `INSUFFICIENT_FUNDS` | O saldo resultante ficaria abaixo do limite de cheque especial. |
| `CURRENCY_MISMATCH` | A moeda da requisição não corresponde à moeda da conta. |
| `IDEMPOTENCY_KEY_REUSED` | A chave de idempotência já foi usada com um corpo de requisição diferente nesta conta. |
| `ENTRY_NOT_REVERSIBLE` | Não é possível estornar este lançamento. |
| `RATE_LIMITED` | A cota de requisições foi excedida. Tente novamente depois do intervalo informado no cabeçalho 'Retry-After'. |
| `INTERNAL_ERROR` | Ocorreu um erro inesperado. Informe o 'correlationId' ao relatar o problema. |
| `SERVICE_UNAVAILABLE` | Uma dependência necessária não respondeu a tempo. A requisição pode ser repetida. |
| `NOT_FOUND` | O recurso solicitado não existe. |
| `METHOD_NOT_ALLOWED` | O método HTTP não é suportado para este recurso. |
| `PAYLOAD_TOO_LARGE` | O corpo da requisição excede o limite de 16 KiB. |
| `UNSUPPORTED_MEDIA_TYPE` | O 'Content-Type' deve ser 'application/json'. |

Quando a recusa nasce de uma regra de negócio, o `detail` é o texto do erro devolvido pelo domínio. Quase sempre coincide com o da tabela, e nestes casos é mais específico:

| `code` | Rota | `detail` na resposta |
|---|---|---|
| `IDEMPOTENCY_KEY_REQUIRED` | `POST` de lançamento e de estorno | O cabeçalho 'Idempotency-Key' é obrigatório. |
| `CURRENCY_MISMATCH` | `POST` de lançamento | A moeda não corresponde à moeda da conta. |
| `ENTRY_NOT_REVERSIBLE` | `POST` de estorno | Não é possível estornar um lançamento que já é um estorno. |
| `IDEMPOTENCY_KEY_REUSED` | `POST` de conta, com chave | A chave de idempotência já foi usada com um corpo de requisição diferente por este chamador. |
| `INVALID_AS_OF` | `GET` do saldo, `asOf` mal formado, sem fuso ou repetido | O parâmetro 'asOf' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo. |
| `INVALID_AS_OF` | `GET` do saldo, `asOf` posterior ao relógio do banco | O parâmetro 'asOf' não pode ser posterior ao instante atual do ledger. |
| `PAYLOAD_TOO_LARGE` | `POST` de lançamento, de estorno e de conta | O corpo da requisição não pode exceder 16384 bytes. |

Os demais textos do domínio (`VALIDATION_FAILED` por moeda, valor, descrição, referência, documento do titular, limite de cheque especial e chave de idempotência malformada) são a rede de segurança: os leitores da API validam antes e respondem com os itens de `errors`, e esses textos só chegariam ao chamador se um leitor deixasse o problema passar.

## Razões de validação

O `VALIDATION_FAILED` de campos inválidos traz a lista `errors`, e a `reason` de cada item é um destes códigos.

| `reason` | Quando |
|---|---|
| `REQUIRED` | Propriedade obrigatória ausente ou `null` |
| `INVALID_FORMAT` | Tipo JSON errado (como número no lugar do texto de `amount`), formato inválido de moeda, documento, valor, `limit` ou parâmetro, instante mal formado (as regras estão em [Datas e fusos horários](api-rest.md#datas-e-fusos-horários)), `description` com caractere de controle, texto com bytes que não são UTF-8 válido, `reference` fora de ASCII visível, `Idempotency-Key` com caractere fora da faixa, repetida ou, na criação de conta, vazia, e parâmetro de consulta repetido |
| `MISSING_TIME_ZONE` | `occurredAt`, `from` ou `to` com data e hora válidas, mas sem `Z` nem deslocamento. O `field` é o campo que faltou |
| `INVALID_JSON` | O corpo não é um único objeto JSON: vazio, `null`, lista, texto solto, JSON malformado, objeto com propriedade repetida ou nome de propriedade com bytes que não são UTF-8 válido, como um `descrição` escrito em cp1252. O `field` é `$` |
| `UNKNOWN_FIELD` | Propriedade desconhecida no corpo ou parâmetro desconhecido na consulta. O `field` é o nome quando ele casa com `^[A-Za-z0-9_]{1,64}$`, e `(unknown)` quando não casa, para que um nome malicioso não volte na resposta |
| `OUT_OF_RANGE` | `amount` menor ou igual a zero, negativo ou acima de `999999999.99`. `overdraftLimit` negativo ou acima do teto. `limit` fora de 1 até o máximo configurado. `occurredAt` anterior a `1970-01-01T00:00:00Z`, já em UTC |
| `TOO_MANY_DECIMALS` | Valor com mais de duas casas decimais |
| `TOO_LONG` | `description` com mais de 140 caracteres, `reference` com mais de 100 ou `Idempotency-Key` com mais de 128 |
| `NOT_ALLOWED` | `type` que não é `CREDIT` nem `DEBIT` |
| `UNSUPPORTED_CURRENCY` | `currency` diferente de `BRL` na criação de conta |
| `IN_THE_FUTURE` | `occurredAt` mais à frente do relógio da API do que a tolerância (5 minutos), medido em UTC |
| `INVALID_CURSOR` | `cursor` vazio, repetido, com formato inválido, com assinatura inválida ou de outra conta |
| `FROM_AFTER_TO` | `from` igual ou posterior a `to`, comparados como instantes e não como horários de parede. O `field` é `from` |

Dois detalhes decidem qual razão aparece. O `amount` é verificado nesta ordem, e a primeira regra que falha vence: formato (`INVALID_FORMAT`), sinal de menos (`OUT_OF_RANGE`), mais de duas casas (`TOO_MANY_DECIMALS`), zero (`OUT_OF_RANGE`) e teto (`OUT_OF_RANGE`), de modo que `"0.00"` e `"-10.00"` dão `OUT_OF_RANGE` e `"10.001"` dá `TOO_MANY_DECIMALS`. Nos instantes, `MISSING_TIME_ZONE` só aparece quando o texto seria válido se tivesse fuso, como `2026-10-05T15:01:45`; qualquer outro defeito de forma, como `2026-10-05T15:01:45-0300`, `2026-02-30T12:00:00Z` ou `2026-10-05`, é `INVALID_FORMAT`.

O `asOf` não tem razão por campo: os dois casos respondem `INVALID_AS_OF`, com uma mensagem que pede o fuso e diz o limite de casas decimais. O `default(DateTimeOffset)` de um cliente .NET (`0001-01-01T00:00:00Z`) cai em `OUT_OF_RANGE` no `occurredAt`. Na consulta, os parâmetros desconhecidos vêm primeiro na lista, depois `from`, `to`, `limit` e `cursor`. Um corpo `chunked` malformado chega à aplicação e responde `VALIDATION_FAILED` sem a lista `errors`.

### Mensagem de cada razão

A `message` é fixa para cada razão e, em `INVALID_FORMAT`, para cada tipo de defeito. O nome do campo, do parâmetro ou do cabeçalho entra entre aspas simples, na grafia do contrato, e os números vêm das constantes do código. O singular vale para 1 ("1 caractere", "1 casa decimal").

| `reason` | `message` |
|---|---|
| `REQUIRED` | O campo 'amount' é obrigatório. |
| `INVALID_FORMAT` | O campo 'currency' tem formato inválido. |
| `INVALID_FORMAT`, em instante | O campo 'occurredAt' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo. |
| `INVALID_FORMAT`, valor monetário (`amount` e `overdraftLimit`) | O campo 'amount' deve ser um texto decimal com ponto, como '80.00', sem símbolo de moeda nem separador de milhar. |
| `INVALID_FORMAT`, `reference` | O campo 'reference' deve ter apenas caracteres ASCII visíveis, sem espaços nem acentos. |
| `INVALID_FORMAT`, `description` com controle | O campo 'description' não pode conter caracteres de controle, como quebra de linha ou tabulação. |
| `INVALID_FORMAT`, texto que não é UTF-8 válido | O campo 'description' deve estar em UTF-8 válido. |
| `INVALID_FORMAT`, parâmetro repetido na consulta | O parâmetro foi enviado mais de uma vez. |
| `INVALID_FORMAT`, cabeçalho repetido | O cabeçalho 'Idempotency-Key' deve ser enviado uma única vez. |
| `INVALID_FORMAT`, `limit` | O campo 'limit' deve ser um número inteiro. |
| `MISSING_TIME_ZONE` | Informe o fuso horário no campo 'occurredAt', por exemplo 'Z' ou '-03:00'. |
| `INVALID_JSON` | O corpo da requisição deve ser um único objeto JSON. |
| `UNKNOWN_FIELD`, no corpo | A propriedade não faz parte do contrato. |
| `UNKNOWN_FIELD`, na consulta | O parâmetro não é suportado. |
| `OUT_OF_RANGE` | O campo 'amount' está fora da faixa permitida. |
| `OUT_OF_RANGE`, `occurredAt` antes do piso | O campo 'occurredAt' está fora da faixa permitida. |
| `OUT_OF_RANGE`, `limit` | O campo 'limit' deve estar entre 1 e 200. |
| `TOO_MANY_DECIMALS` | O campo 'amount' deve ter no máximo 2 casas decimais. |
| `TOO_LONG` | O campo 'description' deve ter no máximo 140 caracteres. |
| `NOT_ALLOWED` | O campo 'type' não é um dos valores permitidos. |
| `UNSUPPORTED_CURRENCY` | O campo 'currency' contém uma moeda não suportada. |
| `IN_THE_FUTURE` | O campo 'occurredAt' não pode estar mais de 5 minutos à frente do relógio do servidor. Com a tolerância em zero: O campo 'occurredAt' não pode estar à frente do relógio do servidor. |
| `INVALID_CURSOR` | O cursor não é válido para esta conta. |
| `FROM_AFTER_TO` | O campo 'from' deve ser anterior a 'to'. |

## Erros fora do catálogo

Um status 4xx que o catálogo não conhece, como 406 e 408, vira `VALIDATION_FAILED` com o status original; o `detail` é o de sempre, "Um ou mais campos são inválidos.", mais amplo do que o problema. O chamador que fecha a conexão antes da resposta aparece, para o servidor, como status 499 sem corpo, e o evento só é registrado. Os erros de protocolo que o servidor responde antes de a requisição chegar à aplicação (URL ou cabeçalhos acima do limite, linha de requisição inválida) saem sem corpo, sem `X-Correlation-Id` e sem os cabeçalhos de segurança.

## De onde vem cada erro

As regras de negócio devolvem um resultado com erro, não lançam exceção. Cada erro tem um `code` do catálogo, uma espécie que a API traduz para o status (validação 400, não encontrado 404, conflito 409, não processável 422) e uma mensagem em português, que vira o `detail`. Um erro cujo `code` não está no catálogo vira 500 `INTERNAL_ERROR`.

A falha inesperada, que chega como exceção, é classificada pelo tratador de exceções da API. Responde 503 com `Retry-After` a falha transitória do PostgreSQL (deadlock, falha de serialização, `lock_timeout`, consulta cancelada, conexões demais, desligamento administrativo, disco cheio e as demais que o Npgsql classifica como transitórias), a falha de conexão, o `TimeoutException`, o cancelamento por prazo, o provedor de chaves de dados pessoais indisponível (só afeta a criação de conta), o tempo de 3 segundos da requisição esgotado e um limite de concorrência saturado. A lista de SQLSTATE e o que se repete estão em [Políticas de resiliência](../08-resiliencia-e-operacao/politicas-de-resiliencia.md).

Qualquer outra exceção, inclusive uma violação de restrição inesperada, responde 500 `INTERNAL_ERROR`, e o detalhe vai só para o log: o tratador nunca copia a mensagem da exceção para a resposta. As mensagens de exceção, de log e de validação de configuração ficam em inglês, porque quem as lê é quem opera o sistema.

O `Retry-After` do 503 vem de `Resilience:ServiceUnavailableRetryAfterSeconds` (1 segundo), e o do 429 é calculado pelo balde de fichas, com mínimo de 1 segundo. Os dois aparecem nos cabeçalhos da resposta, como no exemplo do 503 adiante.

## Exemplos

O pedido está descrito no título de cada exemplo. Os cabeçalhos de segurança foram omitidos, e o JSON está reindentado e decodificado.

Lançamento com um tipo desconhecido, um valor com três casas, uma moeda em minúsculas, um `occurredAt` no futuro e uma propriedade que não existe (`VALIDATION_FAILED` com cinco itens):

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json; charset=utf-8
X-Correlation-Id: 01a10d3c724670ed94d7adeb6593b598

{
  "type": "https://ledger.bank.internal/problems/validation-failed",
  "title": "Falha na validação da requisição",
  "status": 400,
  "detail": "Um ou mais campos são inválidos.",
  "instance": "/v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/entries",
  "code": "VALIDATION_FAILED",
  "correlationId": "01a10d3c724670ed94d7adeb6593b598",
  "traceId": "82309241c5d9b6daafe4cb96d1dc6cd1",
  "errors": [
    {
      "field": "type",
      "reason": "NOT_ALLOWED",
      "message": "O campo 'type' não é um dos valores permitidos."
    },
    {
      "field": "amount",
      "reason": "TOO_MANY_DECIMALS",
      "message": "O campo 'amount' deve ter no máximo 2 casas decimais."
    },
    {
      "field": "currency",
      "reason": "INVALID_FORMAT",
      "message": "O campo 'currency' tem formato inválido."
    },
    {
      "field": "occurredAt",
      "reason": "IN_THE_FUTURE",
      "message": "O campo 'occurredAt' não pode estar mais de 5 minutos à frente do relógio do servidor."
    },
    {
      "field": "note",
      "reason": "UNKNOWN_FIELD",
      "message": "A propriedade não faz parte do contrato."
    }
  ]
}
```

Saldo pedido com `asOf` sem fuso, `2026-10-05T15:03:47.565454` (`INVALID_AS_OF`):

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json; charset=utf-8
X-Correlation-Id: 01a10d8ecd127d32a64ea596e28ab9f3

{
  "type": "https://ledger.bank.internal/problems/invalid-as-of",
  "title": "Instante 'asOf' inválido",
  "status": 400,
  "detail": "O parâmetro 'asOf' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo.",
  "instance": "/v1/accounts/01a10d8e-bc2f-79b6-bd51-8d4819ccb1e4/balance",
  "code": "INVALID_AS_OF",
  "correlationId": "01a10d8ecd127d32a64ea596e28ab9f3",
  "traceId": "8e89e91dc144c9b200fbd1e7796ee322"
}
```

Lançamento sem o cabeçalho `Idempotency-Key`:

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json; charset=utf-8
X-Correlation-Id: 01a10d3c724a7e33bb912be52be7d7ba

{
  "type": "https://ledger.bank.internal/problems/idempotency-key-required",
  "title": "Cabeçalho 'Idempotency-Key' obrigatório",
  "status": 400,
  "detail": "O cabeçalho 'Idempotency-Key' é obrigatório.",
  "instance": "/v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/entries",
  "code": "IDEMPOTENCY_KEY_REQUIRED",
  "correlationId": "01a10d3c724a7e33bb912be52be7d7ba",
  "traceId": "78a8838c3998e60868673ea0c1edf996"
}
```

Débito repetido com a mesma chave e outro valor (`IDEMPOTENCY_KEY_REUSED`):

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/problem+json; charset=utf-8
X-Correlation-Id: 01a10d3c720d75d2987a951fbed7d47b

{
  "type": "https://ledger.bank.internal/problems/idempotency-key-reused",
  "title": "Chave de idempotência reutilizada em outra requisição",
  "status": 422,
  "detail": "A chave de idempotência já foi usada com um corpo de requisição diferente nesta conta.",
  "instance": "/v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/entries",
  "code": "IDEMPOTENCY_KEY_REUSED",
  "correlationId": "01a10d3c720d75d2987a951fbed7d47b",
  "traceId": "c5ffd12be2d6b5acca927c220ca467d0"
}
```

Débito que a conta não cobre (`INSUFFICIENT_FUNDS`):

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/problem+json; charset=utf-8
X-Correlation-Id: 01a10d3c723a7515942c95a7481a780a

{
  "type": "https://ledger.bank.internal/problems/insufficient-funds",
  "title": "Saldo insuficiente",
  "status": 422,
  "detail": "O saldo resultante ficaria abaixo do limite de cheque especial.",
  "instance": "/v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/entries",
  "code": "INSUFFICIENT_FUNDS",
  "correlationId": "01a10d3c723a7515942c95a7481a780a",
  "traceId": "3654d602dbee4135e2fccfba5345761a"
}
```

Segundo estorno do mesmo lançamento (`ENTRY_ALREADY_REVERSED`):

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json; charset=utf-8
X-Correlation-Id: 01a10d3c721b7bd782f0693b3c591c24

{
  "type": "https://ledger.bank.internal/problems/entry-already-reversed",
  "title": "Lançamento já estornado",
  "status": 409,
  "detail": "O lançamento já foi estornado.",
  "instance": "/v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/entries/01a10d3c-71ec-7dd1-b27e-6667b3e58f28/reversals",
  "code": "ENTRY_ALREADY_REVERSED",
  "correlationId": "01a10d3c721b7bd782f0693b3c591c24",
  "traceId": "d0dc9cc41b8619e6800c4de07407d539"
}
```

Pedido com um token malformado (`UNAUTHENTICATED`, com o motivo omitido do cabeçalho):

```http
HTTP/1.1 401 Unauthorized
Content-Type: application/problem+json; charset=utf-8
WWW-Authenticate: Bearer error="invalid_token"
X-Correlation-Id: 01a10d3c7268734fa82daebbdcac0a5b

{
  "type": "https://ledger.bank.internal/problems/unauthenticated",
  "title": "Autenticação necessária",
  "status": 401,
  "detail": "É necessária autenticação para acessar este recurso.",
  "instance": "/v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/balance",
  "traceId": "38896ef0ccc1b0f0492104c54db88dfb",
  "code": "UNAUTHENTICATED",
  "correlationId": "01a10d3c7268734fa82daebbdcac0a5b"
}
```

Lançamento com um token que só tem `ledger.read` (`FORBIDDEN`, com o escopo exigido no cabeçalho):

```http
HTTP/1.1 403 Forbidden
Content-Type: application/problem+json; charset=utf-8
WWW-Authenticate: Bearer error="insufficient_scope", scope="ledger.write"
X-Correlation-Id: 01a10d8ec4ca7ba0be42318b15b2f5e9

{
  "type": "https://ledger.bank.internal/problems/forbidden",
  "title": "Acesso negado",
  "status": 403,
  "detail": "O token não concede acesso a este recurso.",
  "instance": "/v1/accounts/01a10d8e-bc2f-79b6-bd51-8d4819ccb1e4/entries",
  "traceId": "26b0b3a510c3ccd50974ca18b6aa3a8d",
  "code": "FORBIDDEN",
  "correlationId": "01a10d8ec4ca7ba0be42318b15b2f5e9"
}
```

Saldo pedido com o banco de dados parado (`SERVICE_UNAVAILABLE`):

```http
HTTP/1.1 503 Service Unavailable
Content-Type: application/problem+json; charset=utf-8
Retry-After: 1
X-Correlation-Id: 01a10c63083a728faddc0d40fa886d1c

{
  "type": "https://ledger.bank.internal/problems/service-unavailable",
  "title": "Serviço temporariamente indisponível",
  "status": 503,
  "detail": "Uma dependência necessária não respondeu a tempo. A requisição pode ser repetida.",
  "instance": "/v1/accounts/01a10d3e-758f-7dca-a05a-d4d058542c23/balance",
  "code": "SERVICE_UNAVAILABLE",
  "correlationId": "01a10c63083a728faddc0d40fa886d1c",
  "traceId": "6a2315796c285b4cf82f90cb6020a984"
}
```
