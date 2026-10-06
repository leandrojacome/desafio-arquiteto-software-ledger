# Contrato da API REST

A API do ledger é HTTP com JSON, interna ao banco: cinco rotas de negócio sob `/v1` e duas de saúde. A validação do token está em [Autenticação e autorização](../07-consistencia-e-seguranca/autenticacao-e-autorizacao.md), o formato dos erros no [Catálogo de erros](catalogo-de-erros.md) e o caminho interno de cada operação nos [fluxos](../06-fluxos/README.md).

Os exemplos mostram o JSON reindentado e com os escapes desfeitos: no corpo cru, o `'` sai como `\u0027`, como explica a primeira seção. Identificadores, instantes e `traceId` são ilustrativos, e exemplos vizinhos podem vir de contas diferentes. `Authorization` e os cabeçalhos de segurança foram omitidos.

## Convenções

### Formato e idioma

Todas as rotas de negócio ficam sob `/v1`. Pedidos e respostas são JSON em UTF-8, e os erros usam `application/problem+json; charset=utf-8`. O codificador deixa legíveis as letras latinas, as aspas curvas e o `€`, e escapa como `\uXXXX` o que tem significado em HTML (`<`, `>`, `&`, `'`, `"`, `+` e a crase) e o que sai desses alfabetos, como emoji e cirílico. O corpo continua sendo JSON válido e qualquer leitor de JSON recupera o texto original, mas quem lê o corpo cru vê `O campo \u0027amount\u0027 é obrigatório.` no lugar de `O campo 'amount' é obrigatório.`. O texto enviado pelo chamador volta pelo mesmo caminho: uma `description` `Depósito <teste> & 'aspas' +` volta, no corpo cru, como `"description":"Depósito \u003Cteste\u003E \u0026 \u0027aspas\u0027 \u002B"`. O contrato legível por máquina é o documento OpenAPI 3.0 [openapi.v1.json](openapi.v1.json), gerado pela própria API (ver [Documento OpenAPI](#documento-openapi)).

O que uma pessoa lê está em português do Brasil: o `title` e o `detail` de cada erro, a `message` de cada item de `errors` e as descrições do OpenAPI. O que um programa lê continua em inglês: códigos de erro, razões, nomes de campo, de parâmetro, de cabeçalho e de rota, valores de enumeração e tokens de protocolo, como o `error="invalid_token"`. Quem integra decide pelo `code` e pelo status, nunca pela frase, que pode mudar entre versões ([Catálogo de erros](catalogo-de-erros.md), [documento de arquitetura 0037](../03-principios-e-decisoes/documento-arquitetura/0037-idioma-das-mensagens-ao-chamador.md)).

### Autenticação e escopos

Toda rota de negócio exige um JWT em `Authorization: Bearer <token>`. O token carrega a claim `scope`, com os escopos separados por espaço, e a claim `client_id`, que identifica o sistema chamador (de 1 a 128 caracteres ASCII visíveis). Os escopos valem por operação, não por conta: quem tem `ledger.write` registra e estorna lançamento em qualquer conta, e quem tem `ledger.read` lê qualquer conta. O ledger não conhece o titular, e decidir se o chamador pode agir sobre ele é papel do sistema que fica na frente ([Segurança](../07-consistencia-e-seguranca/seguranca.md)).

| Rota | Escopo exigido | Sucesso |
|---|---|---|
| `POST /v1/accounts` | `ledger.write` e `client_id` em `Authorization:AccountProvisioningClients` | 201 |
| `POST /v1/accounts/{accountId}/entries` | `ledger.write` | 201 |
| `POST /v1/accounts/{accountId}/entries/{entryId}/reversals` | `ledger.write` | 201 |
| `GET /v1/accounts/{accountId}/balance` | `ledger.read` | 200 |
| `GET /v1/accounts/{accountId}/entries` | `ledger.read` | 200 |
| `GET /health/live` e `GET /health/ready` | nenhum | 200 |

Token ausente, inválido ou vencido recebe 401 `UNAUTHENTICATED`, com `WWW-Authenticate: Bearer` quando não há token e `Bearer error="invalid_token"` quando há, sem dizer o motivo. Token válido sem o escopo da rota, sem `client_id` ou com `client_id` fora das regras recebe 403 `FORBIDDEN`, com `WWW-Authenticate: Bearer error="insufficient_scope"`. O parâmetro `scope="<escopo>"` só aparece quando o chamador tem um `client_id` válido e o que falta é o escopo da rota. Nos demais casos (sem `client_id`, `client_id` inválido ou, na criação de conta, fora da lista de provisionamento) o `WWW-Authenticate` vem sem `scope`, e o `title` é sempre "Acesso negado".

### Cabeçalhos

| Cabeçalho | Sentido | Regra |
|---|---|---|
| `Authorization` | Pedido | `Bearer <jwt>`. Obrigatório fora das rotas de saúde |
| `Content-Type` | Pedido | `application/json`, com `charset=utf-8` opcional. Obrigatório nos `POST`: outro tipo, ou a falta dele, responde 415. Só o estorno com corpo vazio o dispensa |
| `Idempotency-Key` | Pedido | De 1 a 128 caracteres ASCII visíveis, uma vez só. Obrigatório nos lançamentos e nos estornos, opcional na criação de conta ([Idempotência e hash canônico](idempotencia-e-hash-canonico.md)) |
| `X-Correlation-Id` | Pedido e resposta | Opcional na entrada, aceito se casar com `^[A-Za-z0-9._:-]{8,64}$`. Ausente, repetido ou fora do formato, o valor é descartado e o ledger gera outro, de 32 caracteres hexadecimais. Está sempre na resposta, inclusive em 401, 429 e 503, e é o `correlationId` do erro |
| `traceparent` | Pedido | Padrão W3C, propagado para os traces e para o evento do lançamento. O vínculo com o trace, na resposta, é o `traceId` do erro |
| `Idempotent-Replayed` | Resposta | `true` quando a resposta repete um pedido anterior com a mesma chave. Só nos três `POST` |
| `Location` | Resposta | Nos três `201`. Na criação de conta, `/v1/accounts/{accountId}/balance`. No lançamento e no estorno, `/v1/accounts/{accountId}/entries`, porque a API não lê um lançamento por identificador |
| `Retry-After` | Resposta | Segundos inteiros, de 1 em diante. Em 429, em 503 e na readiness quando ela falha |
| `WWW-Authenticate` | Resposta | Em 401 e 403, como acima |
| `Allow` | Resposta | Em 405, com os métodos que a rota aceita |
| `Cache-Control` | Resposta | `no-store` em toda resposta |

Toda resposta leva ainda `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` e `Referrer-Policy: no-referrer`, e fora de `Development` leva `Strict-Transport-Security: max-age=31536000; includeSubDomains`. Os erros de protocolo que o servidor responde antes de a requisição chegar à aplicação saem sem corpo e sem esses cabeçalhos.

### Formatos e limites

| Tipo | Formato | Observação |
|---|---|---|
| Identificador | UUID versão 7, em 36 caracteres com hifens | Na entrada, minúsculas ou maiúsculas; na saída, minúsculas. Qualquer outra forma (sem hifens, com chaves, texto livre) responde 404 como se a conta ou o lançamento não existisse |
| Valor no pedido | Texto decimal `^[0-9]+(\.[0-9]{1,2})?$`, maior que zero, até `999999999.99` | `"80"` e `"80.00"` são o mesmo valor. Mais de duas casas é erro, nunca arredondamento. Número JSON no lugar do texto é recusado |
| Valor na resposta | Texto decimal com duas casas, como `"920.00"` ou `"-200.00"` | O saldo pode ser negativo, dentro do limite da conta |
| Instante na resposta | `yyyy-MM-ddTHH:mm:ss.ffffffZ`, em UTC, com seis casas | Quem devolve um `recordedAt` como `asOf` deve devolvê-lo como recebeu |
| Instante na entrada (`occurredAt`, `asOf`, `from`, `to`) | ISO 8601 com fuso obrigatório, convertido para UTC na leitura | Detalhes em [Datas e fusos horários](#datas-e-fusos-horários). Na URL, o `+` do deslocamento vai como `%2B` |
| `occurredAt`, tolerância e piso | No máximo 5 minutos à frente do relógio da API (`Ledger:OccurredAtFutureToleranceMinutes`) e não anterior a `1970-01-01T00:00:00Z` | Anterior ao piso é `OUT_OF_RANGE`, inclusive o `default(DateTimeOffset)` do .NET (`0001-01-01T00:00:00Z`) |
| Moeda | Três letras maiúsculas (ISO 4217) | Só `BRL` na criação de conta |
| `description`, `reference` | `description` até 140 caracteres sem controle, `reference` até 100 ASCII visíveis | `description` é aparada, e vazia vale como ausente. O texto livre pode ter acento |
| Corpo do pedido | Até 16 KiB (16384 bytes), senão 413 | Constante da API |
| `limit` do extrato | 1 a 200, padrão 50 | `Ledger:Statement:DefaultLimit` e `MaxLimit` |
| Tempo de uma requisição | 3 segundos, depois 503 | `Resilience:RequestTimeoutSeconds` |

O JSON usa `camelCase` e enumerações em maiúsculas. Propriedade anulável sai sempre, com `null`, e só `settled` (no saldo atual) e `errors` (nos erros sem validação) ficam ausentes. A leitura do corpo é estrita: propriedade desconhecida ou repetida responde 400. A taxa de pedidos e a concorrência por classe de operação também têm limite, e o estouro responde 429 ou 503 ([Limites de taxa e de concorrência](../08-resiliencia-e-operacao/limites-de-taxa-e-concorrencia.md)).

### Ordem em que a API recusa um pedido

Quando mais de uma regra se aplica, vale a resposta da primeira da lista, que é a ordem do pipeline.

1. O `X-Correlation-Id` é lido ou gerado, e por isso toda resposta o leva.
2. A autenticação rejeita o token ausente, inválido ou vencido (401).
3. Os limites de concorrência (503) e de taxa por chamador e por conta (429) respondem antes da autorização: quem esgotou a cota sem ter o escopo recebe 429, não 403.
4. A autorização rejeita o escopo ou o chamador (403).
5. O tamanho do corpo (413) e o `Content-Type` (415), antes de qualquer leitura do conteúdo.
6. O identificador da conta (404 `ACCOUNT_NOT_FOUND`) e, no estorno, o do lançamento (404 `ENTRY_NOT_FOUND`), antes da validação do corpo.
7. A validação do corpo, dos parâmetros e do formato da chave (400 `VALIDATION_FAILED`), com todos os problemas numa lista só.
8. A falta da chave (400 `IDEMPOTENCY_KEY_REQUIRED`), só depois de o corpo estar válido.
9. As regras de negócio, dentro da transação: conta inexistente (404), estorno repetido (409), saldo, moeda, chave reutilizada e lançamento que não se estorna (422).

### Repetição segura

Os três `POST` com `Idempotency-Key` podem ser repetidos com o mesmo conteúdo sem efeito duplicado: a resposta é a original, com `Idempotent-Replayed: true`, e a recusa de negócio não consome a chave. Depois de um 503 ou de um tempo esgotado, o chamador repete o pedido com a mesma chave ([Idempotência e hash canônico](idempotencia-e-hash-canonico.md)).

## Datas e fusos horários

O ledger guarda, calcula e devolve todo instante em UTC. Quem chama pode escrever o instante em qualquer fuso, desde que por inteiro: a API converte para UTC na leitura e devolve sempre UTC com o sufixo `Z`. A regra vale para os quatro campos que carregam um instante na entrada, `occurredAt` no corpo do lançamento e `asOf`, `from` e `to` na consulta. `recordedAt` e `createdAt` são atribuídos pelo ledger e só saem, em UTC. A decisão e as alternativas estão no [documento de arquitetura 0036](../03-principios-e-decisoes/documento-arquitetura/0036-politica-de-fusos-horarios.md).

### O que a entrada aceita

O formato é o ISO 8601 completo, `yyyy-MM-ddTHH:mm:ss`, com fração opcional de 1 a 6 dígitos e fuso obrigatório no fim: a letra `Z` ou um deslocamento `+HH:MM` ou `-HH:MM`, de `-14:00` a `+14:00`. `T` e `Z` são maiúsculos.

A fração aceita de sete a nove dígitos quando os além do sexto são zero, o que não perde precisão e cobre o formato `o` do .NET (`2026-10-05T15:03:47.5654540-03:00`) e os nanossegundos de outras linguagens. Um dígito diferente de zero além do sexto é recusado, nunca arredondado: o ledger guarda microssegundos, e quem tem precisão de 100 nanossegundos, como o `DateTimeOffset.UtcNow`, formata com seis casas (`ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffK")`).

A API apenas soma ou subtrai o deslocamento escrito: não guarda tabela de fusos, não conhece nomes como `America/Sao_Paulo` e não confere se o deslocamento condiz com o lugar de quem chama. Estas entradas são o mesmo instante, `2026-10-05T18:01:45.000000Z`: `2026-10-05T18:01:45Z`, `2026-10-05T18:01:45+00:00`, `2026-10-05T15:01:45-03:00` (Brasília) e `2026-10-05T23:31:45+05:30` (Índia).

### Horário de Brasília

Brasília é UTC-3, ou `-03:00`, o ano inteiro: o Brasil não tem horário de verão desde 2019. Como o deslocamento é do chamador, um período antigo escrito com `-02:00` é aceito pelo que o texto diz. Para converter, some três horas, e a partir das 21:00 a data em UTC já é a do dia seguinte (`2026-03-10T21:00:00-03:00` é `2026-03-11T00:00:00.000000Z`). O saldo no instante de um lançamento, pedido em horário de Brasília, com o `recordedAt` do débito em `2026-10-05T18:03:47.565454Z` (15:03:47 em Brasília), volta com o `asOf` em UTC:

```http
GET /v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/balance?asOf=2026-10-05T15:03:47.565454-03:00 HTTP/1.1
Authorization: Bearer <token>
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
X-Correlation-Id: 01a10d3c95ff747a9ca96782f3091e37

{
  "accountId": "01a10d3c-71ae-740a-b8d9-2fafe5818365",
  "currency": "BRL",
  "balance": "920.00",
  "overdraftLimit": "0.00",
  "asOf": "2026-10-05T18:03:47.565454Z",
  "lastEntryId": "01a10d3c-71ec-7dd1-b27e-6667b3e58f28",
  "settled": true
}
```

### O que a entrada recusa

| Entrada | Resposta |
|---|---|
| Sem fuso, como `2026-10-05T15:01:45` | 400 `VALIDATION_FAILED` com a razão `MISSING_TIME_ZONE` no campo `occurredAt`, `from` ou `to`. No `asOf`, 400 `INVALID_AS_OF`. As duas mensagens pedem o fuso e dão `'Z'` e `'-03:00'` como exemplo |
| Deslocamento sem dois-pontos (`-0300`) ou só com horas (`-03`), fora de `-14:00` a `+14:00`, dígito diferente de zero além da sexta casa ou mais de nove casas, só a data, sem segundos, data ou hora que não existe (`2026-02-30T12:00:00Z`, `24:00:00`, segundo 60), `t` ou `z` minúsculos, espaço no lugar do `T` | 400 com a razão `INVALID_FORMAT` no campo, ou `INVALID_AS_OF` no `asOf` |
| `-00:00` | Recusado como o caso anterior. Na RFC 3339 ele quer dizer "fuso local desconhecido", o contrário do que esta regra pede. Quem quer UTC escreve `Z` ou `+00:00` |
| `occurredAt` mais de 5 minutos à frente do relógio da API | 400 com a razão `IN_THE_FUTURE` |
| `occurredAt` anterior a `1970-01-01T00:00:00Z` | 400 com a razão `OUT_OF_RANGE`, em qualquer deslocamento e depois da conversão para UTC. `asOf`, `from` e `to` não têm piso |
| `asOf` posterior ao relógio do banco | 400 `INVALID_AS_OF`, com a mensagem de instante no futuro |
| `from` igual ou posterior a `to` | 400 com a razão `FROM_AFTER_TO` no campo `from` |

As verificações de futuro e de ordem comparam instantes, não horários de parede: `from` e `to` em fusos diferentes são comparados depois da conversão. Faltar o fuso costuma ser esquecimento, por isso a mensagem diz o que fazer. Um extrato com `from` e `to` sem fuso recebe um item de erro para cada campo. Lançamento com `occurredAt` sem fuso, `2026-10-05T15:01:45` (só a resposta):

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json; charset=utf-8
X-Correlation-Id: 01a10d3c966378d2b09db4e5b2908d27

{
  "type": "https://ledger.bank.internal/problems/validation-failed",
  "title": "Falha na validação da requisição",
  "status": 400,
  "detail": "Um ou mais campos são inválidos.",
  "instance": "/v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/entries",
  "code": "VALIDATION_FAILED",
  "correlationId": "01a10d3c966378d2b09db4e5b2908d27",
  "traceId": "d152c47a6a4a3441d17189c6a995df55",
  "errors": [
    {
      "field": "occurredAt",
      "reason": "MISSING_TIME_ZONE",
      "message": "Informe o fuso horário no campo 'occurredAt', por exemplo 'Z' ou '-03:00'."
    }
  ]
}
```

### Sinal de mais, saída em UTC e hash

Na query string o `+` vale espaço, então um deslocamento positivo vai como `%2B`, por exemplo `asOf=2026-10-05T23:33:47.565454%2B05:30`. Sem o escape a API recebe um espaço, não encontra o fuso e responde 400 `INVALID_AS_OF`. O `Z` e o `-03:00` não precisam de cuidado.

Todo instante devolvido, inclusive os do evento `EntryRegistered`, sai em UTC com seis casas, qualquer que tenha sido o deslocamento da entrada. O cursor do extrato guarda a posição em UTC, e a página seguinte pode ser pedida com os limites escritos em outro fuso. Como a API converte antes de calcular o hash canônico, o mesmo instante com deslocamentos diferentes é o mesmo pedido, e um instante diferente com a mesma chave é 422 `IDEMPOTENCY_KEY_REUSED` ([Idempotência e hash canônico](idempotencia-e-hash-canonico.md)).

### O extrato do dia de Brasília

`from` é inclusivo e `to` é exclusivo, e os dois são instantes. O dia 5 de outubro em Brasília começa à meia-noite de Brasília e termina na meia-noite seguinte, que já é o dia 6, e por isso o `to` é o início do dia seguinte:

```http
GET /v1/accounts/{accountId}/entries?from=2026-10-05T00:00:00-03:00&to=2026-10-06T00:00:00-03:00
```

É a mesma janela de `from=2026-10-05T03:00:00Z&to=2026-10-06T03:00:00Z`, e o dia 5 em UTC é outra, três horas adiantada: um lançamento das 22:30 em Brasília do dia 5 cai no dia 6 em UTC. A API não adivinha qual dos dois dias o chamador quer, e o fuso escrito nos limites deixa isso explícito. Nas fronteiras, um lançamento registrado à meia-noite de Brasília do dia 5 entra na janela, porque o `from` é inclusivo, e o da meia-noite do dia 6 fica de fora, porque o `to` é exclusivo. Um dia sem lançamentos responde 200 com `items` vazio e `nextCursor` nulo.

## Respostas por rota

Todas as rotas de negócio podem devolver `UNAUTHENTICATED` (401), `FORBIDDEN` (403), `RATE_LIMITED` (429), `INTERNAL_ERROR` (500) e `SERVICE_UNAVAILABLE` (503), e os `POST` também `PAYLOAD_TOO_LARGE` (413) e `UNSUPPORTED_MEDIA_TYPE` (415). A tabela lista o que é próprio de cada rota.

| Rota | Sucesso | 400 | 404 | 409 | 422 |
|---|---|---|---|---|---|
| `POST /v1/accounts` | 201 com a conta | `VALIDATION_FAILED` | | | `IDEMPOTENCY_KEY_REUSED` |
| `POST .../entries` | 201 com o lançamento | `VALIDATION_FAILED`, `IDEMPOTENCY_KEY_REQUIRED` | `ACCOUNT_NOT_FOUND` | | `INSUFFICIENT_FUNDS`, `CURRENCY_MISMATCH`, `IDEMPOTENCY_KEY_REUSED` |
| `POST .../reversals` | 201 com o estorno | `VALIDATION_FAILED`, `IDEMPOTENCY_KEY_REQUIRED` | `ACCOUNT_NOT_FOUND`, `ENTRY_NOT_FOUND` | `ENTRY_ALREADY_REVERSED` | `ENTRY_NOT_REVERSIBLE`, `INSUFFICIENT_FUNDS`, `IDEMPOTENCY_KEY_REUSED` |
| `GET .../balance` | 200 com o saldo | `VALIDATION_FAILED`, `INVALID_AS_OF` | `ACCOUNT_NOT_FOUND` | | |
| `GET .../entries` | 200 com a página | `VALIDATION_FAILED` | `ACCOUNT_NOT_FOUND` | | |

## `POST /v1/accounts`

Cria uma conta com saldo zero em BRL. É rota de apoio administrativo e de teste local, porque a conta nasce em outro sistema, e em produção só os `client_id` de `Authorization:AccountProvisioningClients` a usam. O ledger guarda do titular só o documento, cifrado. A criação não gera evento e registra `account.created` na trilha de auditoria.

| Propriedade do corpo | Obrigatória | Regra |
|---|---|---|
| `holderDocument` | Sim | CPF ou CNPJ (inclusive o alfanumérico), com ou sem pontuação, de 11 a 18 caracteres, com os dígitos verificadores conferidos. Nunca volta em claro |
| `currency` | Sim | Só `BRL` |
| `overdraftLimit` | Não | Entre `"0.00"` e `"999999999.99"`. Padrão `"0.00"` |

A resposta `201` traz `accountId`, `currency`, `overdraftLimit`, `holderDocumentMasked` (CPF como `***.***.NNN-**`, CNPJ como `**.***.***/NNNN-**`) e `createdAt`. Com `Idempotency-Key`, repetir o pedido com o mesmo corpo devolve a conta já criada, com o mesmo `Location` e `Idempotent-Replayed: true`, e a mesma chave com outro corpo devolve 422. A chave pertence ao chamador: outro `client_id` com o mesmo valor não a enxerga.

Criação sem chave, com o `X-Correlation-Id` enviado pelo chamador:

```http
POST /v1/accounts HTTP/1.1
Authorization: Bearer <token>
X-Correlation-Id: 5d1b7c0e9a3f4c28b6e1d04f7a92c3b8
Content-Type: application/json

{
  "holderDocument": "529.982.247-25",
  "currency": "BRL",
  "overdraftLimit": "0.00"
}
```

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/balance
X-Correlation-Id: 5d1b7c0e9a3f4c28b6e1d04f7a92c3b8

{
  "accountId": "01a10d3c-71ae-740a-b8d9-2fafe5818365",
  "currency": "BRL",
  "overdraftLimit": "0.00",
  "holderDocumentMasked": "***.***.247-**",
  "createdAt": "2026-10-05T18:03:47.503182Z"
}
```

## `POST /v1/accounts/{accountId}/entries`

Registra um crédito ou um débito. Saldo, lançamento e evento são gravados na mesma transação, e o saldo é decidido por uma única atualização condicional no banco: o débito que a conta não cobre (saldo menor que menos o limite) é recusado com 422 `INSUFFICIENT_FUNDS`, sem gravar nada e sem consumir a chave.

| Propriedade do corpo | Obrigatória | Regra |
|---|---|---|
| `type` | Sim | `CREDIT` soma ao saldo e `DEBIT` subtrai dele |
| `amount` | Sim | Texto decimal maior que zero, com até duas casas, até `"999999999.99"` |
| `currency` | Sim | Três letras maiúsculas. Diferente da moeda da conta: 422 `CURRENCY_MISMATCH` |
| `occurredAt` | Não | Data de negócio, como instante ISO 8601 com fuso ([Datas e fusos horários](#datas-e-fusos-horários)), de `1970-01-01T00:00:00Z` em diante. Não define o saldo. Ausente, vale o `recordedAt` |
| `description` | Não | Até 140 caracteres, sem caractere de controle |
| `reference` | Não | Até 100 caracteres ASCII visíveis |

A resposta `201` é o lançamento gravado:

| Propriedade | Significado |
|---|---|
| `entryId`, `accountId` | Identificadores do lançamento e da conta |
| `accountVersion` | Posição do lançamento na conta, a partir de 1 e sem lacuna. O lançamento N tem `balanceAfter` igual ao do N-1 mais ou menos o valor |
| `type`, `amount`, `currency` | O que foi registrado |
| `balanceAfter` | Saldo da conta logo depois do lançamento |
| `occurredAt` | Data de negócio, em UTC |
| `recordedAt` | Instante do registro, pelo relógio do banco. Define o saldo em um instante e é estritamente crescente dentro da conta |
| `reversesEntryId` | O lançamento que este estorna, ou `null` |
| `description`, `reference` | Os valores informados, ou `null` |

Débito de R$ 80,00 com todos os campos opcionais. O `occurredAt` foi enviado em horário de Brasília, com `-03:00`, e devolvido em UTC:

```http
POST /v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/entries HTTP/1.1
Authorization: Bearer <token>
Idempotency-Key: debit-0001
Content-Type: application/json

{
  "type": "DEBIT",
  "amount": "80.00",
  "currency": "BRL",
  "occurredAt": "2026-10-05T15:01:45-03:00",
  "description": "Pix enviado",
  "reference": "E18236120202610011403s0a1b2c3d4e"
}
```

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/entries
X-Correlation-Id: 01a10d3c71eb7c259998f4927b6127a0

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

A repetição com a mesma chave devolve o mesmo lançamento, e o exemplo está em [Idempotência e hash canônico](idempotencia-e-hash-canonico.md).

## `POST /v1/accounts/{accountId}/entries/{entryId}/reversals`

Estorna um lançamento por inteiro, com um lançamento novo de tipo oposto, mesmo valor e mesma moeda, que aponta para o original em `reversesEntryId`. É a única forma de corrigir um lançamento, porque o livro é imutável. O corpo é opcional e aceita só `description`. O lançamento precisa ser da conta da rota. O estorno não tem `reference`, e o `occurredAt` dele é o `recordedAt`.

| Regra | Resultado |
|---|---|
| O lançamento não existe nesta conta | 404 `ENTRY_NOT_FOUND` |
| O lançamento já foi estornado, inclusive por um pedido concorrente | 409 `ENTRY_ALREADY_REVERSED` |
| O lançamento é ele mesmo um estorno | 422 `ENTRY_NOT_REVERSIBLE` |
| O estorno deixaria o saldo abaixo de menos o limite, como no estorno de um crédito já gasto | 422 `INSUFFICIENT_FUNDS` |

Estorno do débito do exemplo anterior, com uma descrição:

```http
POST /v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/entries/01a10d3c-71ec-7dd1-b27e-6667b3e58f28/reversals HTTP/1.1
Authorization: Bearer <token>
Idempotency-Key: reversal-0001
Content-Type: application/json

{
  "description": "Cobrança em duplicidade confirmada pela conciliação"
}
```

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/entries
X-Correlation-Id: 01a10d3c72127fff8458583d61ba466e

{
  "entryId": "01a10d3c-7213-7d75-b0f1-532fb9958589",
  "accountId": "01a10d3c-71ae-740a-b8d9-2fafe5818365",
  "accountVersion": 3,
  "type": "CREDIT",
  "amount": "80.00",
  "currency": "BRL",
  "balanceAfter": "1000.00",
  "occurredAt": "2026-10-05T18:03:47.604510Z",
  "recordedAt": "2026-10-05T18:03:47.604510Z",
  "reversesEntryId": "01a10d3c-71ec-7dd1-b27e-6667b3e58f28",
  "description": "Cobrança em duplicidade confirmada pela conciliação",
  "reference": null
}
```

## `GET /v1/accounts/{accountId}/balance`

Sem parâmetros, devolve o saldo de agora. Com `asOf`, devolve o `balanceAfter` do último lançamento com `recordedAt` menor ou igual a `asOf`, e uma conta sem lançamento naquele instante tem saldo `"0.00"` e `lastEntryId` nulo. O `asOf` é um instante ISO 8601 com fuso, devolvido em UTC. Sem fuso, malformado, repetido ou posterior ao relógio do banco, a resposta é 400 `INVALID_AS_OF`, e o `detail` diz qual é o problema. Outro parâmetro responde 400 `VALIDATION_FAILED` com a razão `UNKNOWN_FIELD`.

| Propriedade da resposta | Significado |
|---|---|
| `accountId`, `currency` | A conta e a moeda dela |
| `balance` | Saldo, texto decimal com duas casas |
| `overdraftLimit` | Limite atual da conta. O limite não tem histórico: um saldo antigo vem com o limite de agora |
| `asOf` | No saldo atual, o instante em que o banco respondeu. No histórico, o instante pedido, em UTC e com seis casas |
| `lastEntryId` | Último lançamento incluído no saldo, ou `null` |
| `settled` | Só no saldo histórico. Verdadeiro quando `asOf` está fora da janela de acomodação (`Ledger:Balance:SettlingWindowSeconds`, 5 segundos), contada do relógio do banco: o saldo daquele instante não muda mais. Falso nos últimos segundos, em que um lançamento ainda em gravação pode entrar com `recordedAt` anterior |

O ledger compara `asOf` com o relógio do banco, o mesmo que atribui o `recordedAt`, e não com o da instância da API. Um cliente que acabou de receber um `recordedAt` e o devolve como `asOf` a outra instância não toma 400 por um relógio atrasado.

Saldo atual:

```http
GET /v1/accounts/01a10d3c-71ae-740a-b8d9-2fafe5818365/balance HTTP/1.1
Authorization: Bearer <token>
X-Correlation-Id: 4c0a8e61d2f94b7aa03e5b19c6d7f284
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
X-Correlation-Id: 4c0a8e61d2f94b7aa03e5b19c6d7f284

{
  "accountId": "01a10d3c-71ae-740a-b8d9-2fafe5818365",
  "currency": "BRL",
  "balance": "1000.00",
  "overdraftLimit": "0.00",
  "asOf": "2026-10-05T18:03:49.772186Z",
  "lastEntryId": "01a10d3c-7213-7d75-b0f1-532fb9958589"
}
```

Um exemplo de saldo histórico, com `settled` e o `asOf` devolvido em UTC, está em [Horário de Brasília](#horário-de-brasília).

## `GET /v1/accounts/{accountId}/entries`

Devolve o extrato, do lançamento mais recente para o mais antigo, em páginas, ordenado por `recordedAt` e, no empate, por `accountVersion`. A paginação é por posição, com um cursor assinado: a milésima página custa o mesmo que a primeira e não repete nem pula itens, mesmo com escrita concorrente.

| Parâmetro | Regra |
|---|---|
| `from` | Opcional. Limite inferior inclusivo de `recordedAt`, instante ISO 8601 com fuso |
| `to` | Opcional. Limite superior exclusivo de `recordedAt`, no mesmo formato. Precisa ser posterior a `from` como instante, senão 400 `FROM_AFTER_TO`. Sem fuso, 400 `MISSING_TIME_ZONE` |
| `limit` | Opcional, de 1 a 200, padrão 50. Fora da faixa: `OUT_OF_RANGE`. Não inteiro: `INVALID_FORMAT` |
| `cursor` | Opcional. O `nextCursor` da página anterior. Inválido, de outra conta ou repetido: razão `INVALID_CURSOR` |

A resposta traz `items` (os lançamentos, no formato da resposta do registro), `nextCursor` (texto, ou `null` na última página) e `limit` (o tamanho de página aplicado). Conta ou período sem lançamentos responde 200 com `items` vazio. A soma dos valores com sinal dos itens de `[from, to)` é o saldo em `to` menos 1 microssegundo, menos o saldo em `from` menos 1 microssegundo, e é por isso que extrato e saldo contam a mesma história. O formato do cursor está em [Cursor do extrato](cursor-do-extrato.md).

Primeira página de uma conta com três lançamentos, `?limit=2` (só a resposta):

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
X-Correlation-Id: 01a10d3c961d71599da3be9ba64f0ed4

{
  "items": [
    {
      "entryId": "01a10d3c-7213-7d75-b0f1-532fb9958589",
      "accountId": "01a10d3c-71ae-740a-b8d9-2fafe5818365",
      "accountVersion": 3,
      "type": "CREDIT",
      "amount": "80.00",
      "currency": "BRL",
      "balanceAfter": "1000.00",
      "occurredAt": "2026-10-05T18:03:47.604510Z",
      "recordedAt": "2026-10-05T18:03:47.604510Z",
      "reversesEntryId": "01a10d3c-71ec-7dd1-b27e-6667b3e58f28",
      "description": "Cobrança em duplicidade confirmada pela conciliação",
      "reference": null
    },
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
  ],
  "nextCursor": "AQAGXRu0HQeOAAAAAAAAAAKg_hX4rsC8GjSQGu_oPTU2",
  "limit": 2
}
```

A página seguinte, pedida com o `nextCursor` recebido, traz o último lançamento e `nextCursor` nulo:

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
X-Correlation-Id: 01a10d3c962f73bfaa56f7587193f330

{
  "items": [
    {
      "entryId": "01a10d3c-71de-7f0a-b86c-a9be392cacd9",
      "accountId": "01a10d3c-71ae-740a-b8d9-2fafe5818365",
      "accountVersion": 1,
      "type": "CREDIT",
      "amount": "1000.00",
      "currency": "BRL",
      "balanceAfter": "1000.00",
      "occurredAt": "2026-10-05T18:03:47.551216Z",
      "recordedAt": "2026-10-05T18:03:47.551216Z",
      "reversesEntryId": null,
      "description": null,
      "reference": null
    }
  ],
  "nextCursor": null,
  "limit": 2
}
```

## Rotas de saúde

`GET /health/live` responde 200 enquanto o processo estiver de pé e nunca consulta dependência. `GET /health/ready` responde 200 quando o PostgreSQL responde em 1 segundo, o esquema está na versão que o código espera e o processo não recebeu o pedido de encerramento, e 503 com `Retry-After: 5` caso contrário. As chaves de dados pessoais entram como `Degraded`, que continua 200, e a API não olha o broker, porque o RabbitMQ fora do ar não pode derrubar a escrita. As duas rotas são anônimas, aceitam só `GET` e `HEAD` (outro método responde 405 com `Allow: GET, HEAD`) e devolvem só o estado agregado. O Worker responde as mesmas rotas na porta 8081, e o detalhe das verificações está em [Saúde e observabilidade](../08-resiliencia-e-operacao/saude-e-observabilidade.md). Com o PostgreSQL parado e a sonda em cache vencida:

```http
HTTP/1.1 503 Service Unavailable
Content-Type: application/json; charset=utf-8
Retry-After: 5
X-Correlation-Id: 01a10d3ec5647baeb31d1b7d6b417778

{
  "status": "Unhealthy"
}
```

## Documento OpenAPI

O gerador nativo do ASP.NET Core monta o OpenAPI 3.0 a partir do que as rotas declaram no código: as cinco rotas de negócio, os esquemas de requisição e de resposta, o esquema de segurança `Bearer` e o `ProblemResponse`, cujo campo `code` enumera o catálogo de erros. As rotas de saúde ficam de fora, por serem contrato operacional, e as descrições estão em português.

A API serve o documento em `GET /openapi/v1.json`, sem token, só em `Development` e `Testing`. Nos outros ambientes a rota não existe: o chamador recebe o 401 de toda rota desconhecida sem token e 404 com token. É a única rota anônima além das de saúde. O arquivo [openapi.v1.json](openapi.v1.json) é o mesmo documento.

`OpenApiContractTests.TheGeneratedDocument_IsTheVersionedFile` gera o documento, compara com `docs/05-contratos/openapi.v1.json` e falha com a instrução de regenerar quando há diferença. Depois de uma mudança intencional de contrato, este comando roda o mesmo teste e grava o documento no lugar de comparar:

```bash
LEDGER_OPENAPI_UPDATE=true dotnet test tests/Ledger.Api.IntegrationTests --filter "FullyQualifiedName~OpenApiContractTests"
```

## Compatibilidade e versionamento

A versão maior fica no caminho, e dentro dela vale a regra do que só acrescenta. O `/v1` é a única versão que existe.

| Mudança | Compatível? |
|---|---|
| Rota nova, relaxar uma validação, propriedade nova numa resposta ou num evento | Sim. Quem consome precisa ignorar o que não conhece |
| Propriedade opcional nova no pedido | Sim, mas o servidor novo precisa estar no ar antes de o cliente enviá-la, porque o antigo recusa propriedade desconhecida |
| Código de erro novo para uma condição nova | Sim. O chamador deve ter um ramo padrão para códigos que não conhece |
| Remover ou renomear propriedade, mudar tipo, formato ou significado, apertar uma validação, mudar o status ou o `code` de uma condição existente | Não |
| Valor novo numa enumeração que o chamador interpreta, como um `type` além de `CREDIT` e `DEBIT` | Não |

Uma mudança incompatível vira `/v2`, que convive com o `/v1` até os consumidores migrarem. O pedido é estrito e a resposta tolerante de propósito: quem escreve no ledger precisa ser exato, e quem lê dele precisa aguentar crescimento.
