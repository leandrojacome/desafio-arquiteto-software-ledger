# Requisitos funcionais

Os requisitos vão de `FR-01` a `FR-24`. Cada um traz o enunciado e critérios de aceite no formato Dado, Quando, Então, e os testes de cada um estão em [Rastreabilidade de requisitos](rastreabilidade.md). O FR-19 (eventos) e o FR-20 (conferência de integridade) são os dois que a escrita dispensa para funcionar: sem consumidor, o outbox guarda os eventos, e a conferência só observa os dados. O que [Contexto de negócio](contexto-de-negocio.md#escopo) deixa fora do escopo também não está aqui.

Valores monetários trafegam como texto decimal com duas casas, nunca como ponto flutuante. Todo instante trafega em ISO 8601, com fuso horário obrigatório na entrada (`Z` ou um deslocamento como `-03:00`, o horário de Brasília) e sempre em UTC, com o sufixo `Z`, na saída. O formato de cada campo está em [Contrato da API REST](../05-contratos/api-rest.md), os códigos de erro em [Catálogo de erros](../05-contratos/catalogo-de-erros.md) e a regra por trás de cada requisito em [Regras de negócio](regras-de-negocio.md).

## Contas

**FR-01 Criar conta.** `POST /v1/accounts` cria uma conta com o documento do titular (CPF ou CNPJ válido), a moeda (BRL) e o limite de cheque especial, opcional e igual a zero quando omitido. O ledger não recebe nem guarda o nome do titular. O saldo inicial é zero, não há lançamento de abertura e a criação não gera evento. A rota é de uso administrativo e de testes locais, porque a conta nasce em outro sistema, e exige o escopo `ledger.write` e um chamador constante de `Authorization:AccountProvisioningClients`. A `Idempotency-Key` é opcional: com a chave, repetir o pedido devolve a mesma conta.

- Dado um corpo válido sem `overdraftLimit`, quando um chamador autorizado cria a conta, então recebe 201 com `overdraftLimit` "0.00" e o documento só mascarado em `holderDocumentMasked`, e o saldo consultado em seguida é "0.00".
- Dado um documento inválido, uma moeda diferente de BRL ou um `overdraftLimit` negativo, com mais de duas casas ou acima de 999999999.99, quando a conta é criada, então a resposta é 400 `VALIDATION_FAILED` com o campo e o motivo, e nada é gravado.
- Dado um chamador sem `ledger.write` ou fora da lista de provisionamento, quando tenta criar a conta, então a resposta é 403 `FORBIDDEN` e o corpo não é lido.
- Dado o mesmo `Idempotency-Key` e o mesmo corpo do mesmo chamador, quando o pedido é repetido, então a resposta é a conta original com `Idempotent-Replayed: true`, e com outro corpo é 422 `IDEMPOTENCY_KEY_REUSED`.

## Lançamentos

**FR-02 Registrar crédito.** `POST /v1/accounts/{accountId}/entries` com `type` igual a `CREDIT` soma o valor ao saldo.

- Dada uma conta com saldo "100.00", quando chega um crédito de "25.50" com uma `Idempotency-Key` nova, então a resposta é 201, o lançamento tem `balanceAfter` "125.50" e o saldo atual passa a ser "125.50".

**FR-03 Registrar débito.** Com `type` igual a `DEBIT`, a rota subtrai o valor, desde que o saldo resultante não fique abaixo de menos o limite da conta (BR-06).

- Dada uma conta com saldo "100.00" e limite "0.00", quando chega um débito de "100.00", então a resposta é 201 com `balanceAfter` "0.00".
- Dada uma conta com saldo "100.00" e limite "50.00", quando chega um débito de "150.00", então a resposta é 201 com `balanceAfter` "-50.00".

**FR-04 Recusar débito sem saldo.** O débito que levaria a conta abaixo do limite é recusado por inteiro, sem lançamento parcial. A recusa por regra de negócio não consome a chave de idempotência: só uma operação confirmada a consome.

- Dada uma conta com saldo "100.00" e limite "0.00", quando chega um débito de "100.01", então a resposta é 422 `INSUFFICIENT_FUNDS`, nenhum lançamento é criado e o saldo continua "100.00".
- Dado esse débito recusado, quando o chamador repete a chamada com a mesma chave depois de um crédito que cobre o valor, então o débito é avaliado de novo e pode ser aceito.

**FR-05 Validar o pedido.** O `amount` é texto decimal maior que zero, com no máximo duas casas e no máximo "999999999.99", um limite de sanidade contra erro de digitação. O `type` é `CREDIT` ou `DEBIT`, em maiúsculas, e a `currency` é a da conta. O `occurredAt` é um instante ISO 8601 com fuso horário, convertido para UTC, que não pode estar mais de 5 minutos à frente do relógio da API nem ser anterior a `1970-01-01T00:00:00Z`, e que vale o instante do registro se omitido. A `description` tem até 140 caracteres sem caracteres de controle, e a `reference` (o identificador no sistema de origem) até 100 caracteres ASCII visíveis. Campos desconhecidos são recusados, o corpo tem no máximo 16 KiB (413 `PAYLOAD_TOO_LARGE`) e o `Content-Type` é JSON (415 `UNSUPPORTED_MEDIA_TYPE`). Todos os problemas de um pedido saem juntos em uma resposta só. A mesma validação de instante vale para `asOf`, `from` e `to`.

- Dado um `amount` "0.00", "-10.00", "10.001", "abc" ou um número JSON, quando o lançamento é enviado, então a resposta é 400 `VALIDATION_FAILED` indicando o campo e o motivo, e nada é gravado.
- Dado um `occurredAt` 5 minutos e 1 microssegundo à frente do relógio da API, quando o lançamento é enviado, então a resposta é 400 `VALIDATION_FAILED`, e com exatamente 5 minutos é aceito.
- Dado um `occurredAt` anterior a `1970-01-01T00:00:00Z`, como o `0001-01-01T00:00:00Z` que um cliente .NET envia para o `default(DateTimeOffset)`, quando o lançamento é enviado, então a resposta é 400 `VALIDATION_FAILED` com a razão `OUT_OF_RANGE`, e nada é gravado.
- Dado um `occurredAt` com sete a nove casas de fração, quando os dígitos depois da sexta são zero (`2026-10-05T15:03:47.5654540-03:00`), então o lançamento é aceito, e com um dígito diferente de zero ali a resposta é 400 `VALIDATION_FAILED` com a razão `INVALID_FORMAT`.
- Dado um `occurredAt` sem fuso horário, como `2026-10-05T15:01:45`, quando o lançamento é enviado, então a resposta é 400 `VALIDATION_FAILED` com a razão `MISSING_TIME_ZONE` e uma mensagem que pede o fuso, e nada é gravado.
- Dada uma conta em BRL, quando o lançamento chega em outra moeda, como `currency` "EUR", então a resposta é 422 `CURRENCY_MISMATCH`.
- Dado um `accountId` inexistente, quando o lançamento chega, então a resposta é 404 `ACCOUNT_NOT_FOUND`.

## Idempotência

**FR-06 Exigir a chave.** Todo `POST` que cria lançamento, estorno inclusive, exige o cabeçalho `Idempotency-Key`, com 1 a 128 caracteres ASCII visíveis. Um UUID ou o identificador da ordem de origem servem. A chave vale por conta: a mesma chave em duas contas são duas operações independentes.

- Dado um pedido de lançamento sem o cabeçalho, quando ele chega, então a resposta é 400 `IDEMPOTENCY_KEY_REQUIRED`.
- Dada uma chave de 129 caracteres, com caractere fora do ASCII visível ou enviada duas vezes, quando o pedido chega, então a resposta é 400 `VALIDATION_FAILED` apontando o cabeçalho.

**FR-07 Repetir sem duplicar.** A mesma chave com o mesmo corpo devolve o resultado da primeira vez, sem criar nada novo. Duas chamadas idênticas simultâneas produzem um único lançamento.

- Dado um débito de "80.00" confirmado com a chave K, quando a mesma chamada chega de novo, então a resposta tem o mesmo status e o mesmo `entryId`, traz `Idempotent-Replayed: true` e o extrato continua com um único lançamento.
- Dado um débito confirmado com `occurredAt` `2026-10-05T15:01:45-03:00`, quando a mesma chamada chega com `2026-10-05T18:01:45Z`, então é a repetição do mesmo pedido, com o mesmo `entryId` e `Idempotent-Replayed: true`.
- Dadas 40 chamadas idênticas simultâneas com a mesma chave, quando todas são processadas, então existe um único lançamento e as 40 recebem o mesmo corpo.

**FR-08 Barrar chave reutilizada com outro pedido.** A mesma chave com conteúdo diferente é erro do chamador. Não vira um lançamento novo e também não devolve o resultado antigo como se respondesse ao pedido novo. A resposta não revela o que mudou.

- Dada a chave K usada em um débito de "80.00", quando K chega em um débito de "90.00", em outro tipo, outra moeda, outra descrição, outro instante ou em um estorno, então a resposta é 422 `IDEMPOTENCY_KEY_REUSED` e nada é gravado.

## Concorrência

**FR-09 Escritas simultâneas na mesma conta.** O ledger nunca perde uma atualização e nunca deixa o saldo abaixo do limite, por mais chamadas que cheguem juntas. O mecanismo está no [documento de arquitetura 0004](../03-principios-e-decisoes/documento-arquitetura/0004-saldo-corrente-com-update-condicional.md), e o comportamento sob carga sustentada na mesma conta é o assunto do [NFR-01](requisitos-nao-funcionais.md).

- Dada uma conta com saldo "100.00" e limite "0.00", quando 10 débitos de "20.00", com chaves diferentes, chegam ao mesmo tempo, então exatamente 5 recebem 201 e 5 recebem 422 `INSUFFICIENT_FUNDS`, o saldo final é "0.00" e os cinco lançamentos têm `balanceAfter` "80.00", "60.00", "40.00", "20.00" e "0.00" em ordem de registro.
- Dadas operações concorrentes de crédito e débito sobre a mesma conta, quando terminam, então a variação do saldo é igual à soma assinada dos lançamentos aceitos.

## Estorno e imutabilidade

**FR-10 Estornar um lançamento.** `POST /v1/accounts/{accountId}/entries/{entryId}/reversals` cria um lançamento de tipo oposto e mesmo valor, com `reversesEntryId` apontando para o original. O corpo é opcional e aceita só `description`.

- Dado um débito E de "150.00" que levou o saldo a "850.00", quando o estorno é pedido, então a resposta é 201 com um lançamento `CREDIT` de "150.00", `reversesEntryId` igual a E e `balanceAfter` "1000.00".
- Dado o estorno feito, quando o extrato é lido, então E continua com o mesmo valor, o mesmo `balanceAfter` e o mesmo `recordedAt`.

**FR-11 Estornar uma vez, e só o que pode ser estornado.** Cada lançamento é estornável uma única vez, e um estorno não pode ser estornado. Não há estorno parcial.

- Dado um lançamento já estornado, quando chega outro pedido de estorno com chave diferente, então a resposta é 409 `ENTRY_ALREADY_REVERSED`, e com a mesma chave é a repetição da resposta original.
- Dado um lançamento que é ele próprio um estorno, quando se pede o estorno dele, então a resposta é 422 `ENTRY_NOT_REVERSIBLE`.
- Dado um `entryId` que não pertence à conta da rota, quando o estorno é pedido, então a resposta é 404 `ENTRY_NOT_FOUND`.
- Dados 20 estornos concorrentes do mesmo lançamento, quando são processados, então um recebe 201 e os outros 19 recebem 409.

**FR-12 Estorno obedece à regra de saldo.** O estorno de um crédito é um débito, e a conta não pode ficar abaixo do limite por causa dele. Se o crédito já foi gasto, o estorno é recusado e o caso fica com o processo de cobrança do banco (a pergunta sobre um estorno forçado está em [Questões em aberto](../03-principios-e-decisoes/questoes-em-aberto.md)).

- Dado um crédito de "300.00" cujo valor já foi gasto, deixando saldo "20.00", quando o estorno do crédito é pedido, então a resposta é 422 `INSUFFICIENT_FUNDS` e o crédito continua sem estorno, com a chave livre.

**FR-13 Lançamentos não mudam.** Nenhuma rota altera ou remove lançamento, o código não tem `UPDATE` nem `DELETE` sobre eles e o banco rejeita a tentativa, até para o papel da API ([documento de arquitetura 0002](../03-principios-e-decisoes/documento-arquitetura/0002-ledger-imutavel-somente-insercao.md)).

- Dado qualquer lançamento confirmado, quando o tempo passa e outras operações acontecem na conta, então os campos dele lidos no extrato são sempre os mesmos.
- Dado um `PUT`, `PATCH` ou `DELETE` contra as rotas de lançamento, quando a chamada chega, então nada é alterado e a resposta é 405 (rotas de registro e de estorno) ou 404 (caminho de um lançamento).
- Dado um `UPDATE`, `DELETE` ou `TRUNCATE` sobre `ledger_entries` direto no banco, quando executado, então o banco o rejeita, pelo gatilho ou pela falta de privilégio do papel.

## Consultas

**FR-14 Saldo atual.** `GET /v1/accounts/{accountId}/balance` devolve o saldo de agora, com `balance`, `currency`, `overdraftLimit`, `asOf` (o instante da resposta, pelo relógio do banco) e `lastEntryId` (nulo quando a conta não tem lançamentos). A resposta não é guardada em cache.

- Dada uma conta com saldo "850.00", quando o saldo é consultado, então a resposta é 200 com `balance` "850.00" e `lastEntryId` igual ao último lançamento.
- Dada uma conta sem lançamentos, quando o saldo é consultado, então `balance` é "0.00" e `lastEntryId` é nulo.

**FR-15 Saldo em um instante.** Com `?asOf=` em ISO 8601 com fuso horário, convertido para UTC, a rota devolve o `balanceAfter` do último lançamento com `recorded_at` menor ou igual ao instante, comparação inclusiva (o motivo de não usar a data de negócio está na BR-08). A resposta só é repetível para instantes fora da janela de acomodação de 5 segundos: um lançamento recebe o `recorded_at` antes de confirmar e, dentro da janela, a mesma consulta pode enxergar um lançamento que ainda não tinha confirmado. Por isso a resposta traz `settled`, verdadeiro quando o instante já saiu da janela, e quem precisa de um número que não muda usa só respostas com `settled` verdadeiro ([documento de arquitetura 0018](../03-principios-e-decisoes/documento-arquitetura/0018-monotonicidade-do-recorded-at-e-janela-de-acomodacao.md)).

- Dados lançamentos registrados às 10:00:00Z (`balanceAfter` "500.00") e às 10:05:00Z ("350.00"), quando se consulta `asOf` 10:04:59Z, então `balance` é "500.00", e com `asOf` 10:05:00Z é "350.00".
- Dado um `asOf` anterior ao primeiro lançamento da conta, quando a consulta chega, então a resposta é 200 com `balance` "0.00" e `lastEntryId` nulo.
- Dado um `asOf` dos últimos 5 segundos, quando a consulta chega, então `settled` é falso. Dado um `asOf` mais antigo, `settled` é verdadeiro e a mesma consulta repetida devolve o mesmo valor mesmo com escritas no meio.

**FR-16 Recusar instante inválido.** O `asOf` precisa ser um instante ISO 8601 com fuso horário e não pode estar no futuro do relógio do banco. Sem fuso, o ledger teria de adivinhar o horário do chamador, e um instante futuro pediria um saldo que ainda não existe.

- Dado um `asOf` sem fuso horário, mal formado, com `-00:00` ou repetido, quando a consulta chega, então a resposta é 400 `INVALID_AS_OF`, e para o instante sem fuso a mensagem pede o fuso.
- Dado um `asOf` com deslocamento válido, como `-03:00`, quando a consulta chega, então a resposta é 200, e o `asOf` da resposta vem em UTC. A regra de casas de fração é a do FR-05, com `INVALID_AS_OF` no lugar de `INVALID_FORMAT`.
- Dado um `asOf` posterior ao relógio do banco, quando a consulta chega, então a resposta é 400 `INVALID_AS_OF`, e para uma conta inexistente a resposta é 404.

**FR-17 Extrato paginado.** `GET /v1/accounts/{accountId}/entries` devolve os lançamentos do mais recente para o mais antigo (por `recorded_at` e, no empate, por versão da conta), em páginas por cursor. O `limit` vai de 1 a 200, com padrão 50, e o cursor é opaco, assinado e amarrado à conta ([Cursor do extrato](../05-contratos/cursor-do-extrato.md)).

- Dada uma conta com 120 lançamentos, quando o extrato é lido com `limit` 50 seguindo `nextCursor`, então vêm três páginas (50, 50 e 20), `nextCursor` é nulo na última e nenhum lançamento aparece duas vezes ou falta.
- Dados novos lançamentos chegando entre a leitura da primeira e da segunda página, quando se segue o cursor, então a segunda página continua exatamente de onde a primeira parou.

**FR-18 Extrato por período.** `from` e `to` filtram por `recorded_at`, o mesmo relógio do saldo, de modo que extrato e saldo sempre conversam. `from` é inclusivo e `to` é exclusivo. Os dois são instantes ISO 8601 com fuso horário, convertidos para UTC, e o dia civil de Brasília se pede com `-03:00` nos dois limites.

- Dado `from` 2026-10-01T00:00:00-03:00 e `to` 2026-10-02T00:00:00-03:00 (o dia 1º de outubro em Brasília, que em UTC vai de 2026-10-01T03:00:00Z a 2026-10-02T03:00:00Z), quando o extrato é lido, então voltam só os lançamentos com `recorded_at` nessa janela, inclusive o registrado às 22:30 de Brasília, que em UTC já é dia 2.
- Dado `from` ou `to` sem fuso horário, quando a chamada chega, então a resposta é 400 `VALIDATION_FAILED` com a razão `MISSING_TIME_ZONE` no campo que faltou.
- Dado `from` igual ou posterior a `to`, um `limit` fora de 1 a 200 ou um cursor adulterado, quando a chamada chega, então a resposta é 400 `VALIDATION_FAILED`.

## Eventos e integridade

**FR-19 Publicar eventos de lançamento.** Cada lançamento confirmado, estornos inclusive, gera um evento `EntryRegistered`, gravado na mesma transação, que o Worker publica no RabbitMQ com entrega pelo menos uma vez. Quem consome deduplica pelo `message_id`, igual ao `eventId` do corpo, e não deve depender da ordem de chegada: o evento traz `accountVersion` e `balanceAfter` para ordenar e `recordedAt` para o instante. Recusas, repetições e conflitos de chave não geram evento. O atraso esperado entre o commit e a publicação é o [NFR-13](requisitos-nao-funcionais.md). A justificativa está no [documento de arquitetura 0008](../03-principios-e-decisoes/documento-arquitetura/0008-rabbitmq-entrega-ao-menos-uma-vez.md), e o contrato em [Contrato de eventos](../05-contratos/eventos.md).

- Dado o broker no ar, quando um lançamento é confirmado, então o evento chega à fila com `eventId` único e o `correlationId` da requisição.
- Dado o broker fora do ar, quando lançamentos são registrados, então todos recebem 201 no tempo normal, e quando o broker volta os eventos são publicados.
- Dado o mesmo evento entregue duas vezes a um consumidor, quando ele processa, então o efeito é aplicado uma vez, porque o `message_id` é o mesmo.
- Dado que nenhum consumidor declarou fila, quando lançamentos são confirmados, então os eventos esperam na fila de retenção do Worker, sem duplicata, e se nenhuma fila estiver ligada à exchange o evento continua no outbox e não é marcado como publicado.

**FR-20 Conferir a integridade.** O Worker confere três coisas: o saldo guardado da conta é igual ao `balance_after` do último lançamento, cada `balance_after` é o anterior mais o valor assinado do lançamento (sem lacuna de versão nem retrocesso de `recorded_at`) e nenhum saldo está abaixo de menos o limite. Contas com movimento recente são conferidas a cada 5 minutos e todas as contas uma vez por dia (`Integrity:RecentIntervalMinutes` e `Integrity:FullIntervalHours`). O Worker registra cada execução e cada violação na trilha, escreve a violação também em log de erro e nunca corrige nada sozinho. As regras de alerta estão em [Indicadores, objetivos e alertas](../08-resiliencia-e-operacao/slos-e-alertas.md).

- Dado um ledger sem adulteração, quando a conferência roda, então reporta zero violações e registra a execução.
- Dado um saldo corrompido por teste (alteração direta no banco, fora do produto), quando a conferência roda, então reporta a conta e a verificação que falhou e não altera nenhum dado.

## Acesso e rastreio

**FR-21 Autenticar e autorizar.** Toda rota de negócio exige um JWT válido, obtido por client credentials, com o `client_id` do chamador. O escopo `ledger.read` autoriza as consultas, `ledger.write` autoriza criar conta, lançar e estornar, e os dois são independentes. A validação do token está em [Autenticação e autorização](../07-consistencia-e-seguranca/autenticacao-e-autorizacao.md).

- Dado um pedido sem token, com token mal formado ou com token vencido, quando chega, então a resposta é 401 `UNAUTHENTICATED`.
- Dado um token só com `ledger.read`, quando ele tenta um lançamento, então a resposta é 403 `FORBIDDEN` e nada é gravado.
- Dado um token válido sem `client_id`, quando chama qualquer rota de negócio, então a resposta é 403.

**FR-22 Correlacionar tudo.** Cada chamada tem um `X-Correlation-Id`. Se o chamador manda um valor de 8 a 64 caracteres entre letras, dígitos, ponto, sublinhado, dois-pontos e hífen, o ledger o usa. Se não manda, ou manda outro formato, o ledger gera um. O valor volta na resposta, inclusive nos erros, e aparece nos logs, na trilha de auditoria, na linha do lançamento e no evento.

- Dado um pedido com `X-Correlation-Id` "abc-12345", quando ele é processado, então a resposta devolve "abc-12345", e a linha do lançamento e o evento carregam o mesmo valor.
- Dado um valor fora do formato, quando o pedido chega, então a resposta traz um identificador gerado de 32 hexadecimais e o valor recebido não chega aos logs.

**FR-23 Registrar quem fez o quê.** Toda escrita aceita deixa rastro de quem a pediu na mesma transação, sem uma segunda escrita por lançamento: a própria linha de `ledger_entries` é o registro e carrega o `client_id` autenticado, o `correlation_id`, o `recorded_at` do relógio do banco, a conta e o tipo. As ações administrativas e de segurança (criação de conta, recusa de escrita por autorização, decifragem durante a recifragem, ativação e rotação de chaves, execução da conferência de integridade e violações encontradas) vão para a trilha `audit_log`, de catálogo fechado. As recusas por regra de negócio ficam em log estruturado com o mesmo identificador, e as consultas de saldo e de extrato deixam um registro estruturado de quem consultou. O catálogo está em [Trilha de auditoria](../07-consistencia-e-seguranca/trilha-de-auditoria.md).

- Dado um lançamento confirmado, quando o auditor consulta `ledger_entries`, então encontra a linha com esses cinco dados, e não existe lançamento sem a linha correspondente.
- Dado um token só com `ledger.read` chamando uma rota de escrita, quando a recusa acontece, então a trilha ganha uma linha `authorization.denied_write` com a rota, o escopo e o motivo.

**FR-24 Degradar de forma controlada.** `GET /health/live` responde 200 enquanto o processo estiver de pé. `GET /health/ready` responde 200 só com o banco acessível, o esquema compatível com o código e o processo fora de desligamento, e 503 com `Retry-After` caso contrário. A única exceção deliberada é o provedor de chaves de dados pessoais: quando ele cai, a prontidão fica `Degraded` e continua 200, porque as chaves só servem à criação de conta, que responde 503, enquanto lançamentos e consultas seguem ([documento de arquitetura 0019](../03-principios-e-decisoes/documento-arquitetura/0019-readiness-nao-depende-do-provedor-de-chaves.md)). Com o banco fora, as rotas de negócio respondem 503 `SERVICE_UNAVAILABLE` com `Retry-After` dentro do prazo da requisição, em vez de ficarem penduradas. Quem passa da sua cota recebe 429 `RATE_LIMITED` com `Retry-After` sem afetar os demais, e os limites de concorrência impedem que extratos pesados esgotem a capacidade das escritas ([documento de arquitetura 0011](../03-principios-e-decisoes/documento-arquitetura/0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md), [Limites de taxa e de concorrência](../08-resiliencia-e-operacao/limites-de-taxa-e-concorrencia.md)).

- Dado o PostgreSQL parado, quando se chama `/health/ready` e um `POST` de lançamento, então o primeiro devolve 503, o segundo devolve 503 com `Retry-After`, e `/health/live` continua 200.
- Dado o RabbitMQ parado, quando se registra um lançamento, então a resposta é 201 normal e o `/health/ready` da API continua 200.
- Dado um chamador que excede a cota, quando chega a requisição seguinte, então a resposta é 429 com `Retry-After`, e a cota de outro chamador não é afetada.

## Leia também

- [Requisitos não funcionais](requisitos-nao-funcionais.md): metas de vazão, latência, disponibilidade e recuperação.
- [Idempotência e hash canônico](../05-contratos/idempotencia-e-hash-canonico.md): o que a chave compara e como.
