# Documento de arquitetura 0023: O `client_id` entra no hash do pedido

## Contexto

A chave de idempotência é única por conta e não por chamador ([documento de arquitetura 0006](0006-idempotencia-por-chave-e-hash.md)), e o hash guardado junto com ela decide se um encontro com uma chave já usada é repetição ou reúso. Se o hash não levar o chamador, dois sistemas independentes, o Pix e os cartões por exemplo, que usem chaves curtas ou sequenciais na mesma conta e mandem o mesmo corpo (mesmo valor, mesma moeda, sem descrição) chegam ao mesmo hash. O segundo recebe 201 com `Idempotent-Replayed: true` e o `entryId` do primeiro, e acredita que o débito dele foi registrado quando nenhum dinheiro se moveu por ele. É o dano silencioso que a idempotência quer evitar, só que entre chamadores em vez de dentro de um. Também deixaria um chamador confirmar que existe um lançamento de outro.

Recomendar UUID como chave continua certo, mas a segurança do dinheiro não deve depender da disciplina de quem chama.

## Decisão

O `clientId`, a claim `client_id` do token, entra na cadeia canônica do hash logo depois do `accountId`, no registro e no estorno. Quando dois chamadores se encontram na mesma dupla (conta, chave), o resultado é 422 `IDEMPOTENCY_KEY_REUSED` e nada é gravado. A unicidade no banco continua `(account_id, idempotency_key)`. A cadeia e os vetores de teste estão em [Idempotência e hash canônico](../../05-contratos/idempotencia-e-hash-canonico.md), e o `CanonicalRequestHashTests` os confere.

A cadeia usa `\n` como separador, e a concatenação só é inequívoca se o `client_id` não contiver quebra de linha. Por isso a API recusa, na autenticação, o `client_id` que não seja composto de 1 a 128 caracteres ASCII visíveis (motivo `invalid_client_id`), o que exclui o separador.

## Alternativas descartadas

- Chave primária `(account_id, client_id, idempotency_key)`. Dá a cada chamador o seu próprio espaço de chaves e é a mais correta se os chamadores forem tratados como inquilinos separados. Pede migrar a chave primária de `idempotency_keys` ([documento de arquitetura 0017](0017-retencao-de-35-dias-das-chaves-de-idempotencia.md)) e muda a chave de busca da repetição. Serve como evolução, para o dia em que dois chamadores precisarem usar a mesma chave na mesma conta de propósito.
- Só exigir UUID dos chamadores. Não custa nada e deixa a defesa inteira nas mãos de quem pode errar.
- Guardar o `client_id` na linha da chave e comparar à parte. Mesmo efeito do hash, com uma coluna a mais e a comparação espalhada em dois lugares.

## Consequências

Um mesmo sistema que passe a usar outra credencial, e portanto outro `client_id`, e repita um pedido antigo com a mesma chave recebe 422 em vez de uma repetição, dentro da janela de 35 dias. A troca de credencial no chamador deve esperar os pedidos em voo. Se a taxa de 422 por conflito de chave subir depois de uma rotação de credenciais, ou se dois chamadores precisarem dividir chaves, a chave primária com `client_id` passa a valer a migração.
