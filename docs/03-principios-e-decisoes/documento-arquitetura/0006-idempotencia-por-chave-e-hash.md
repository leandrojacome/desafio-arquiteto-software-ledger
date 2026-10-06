# Documento de arquitetura 0006: Idempotência por chave e hash

## Contexto

Quem chama o ledger (app, Pix, cartões) vive numa rede que falha. Uma requisição que estoura o timeout pode ter sido gravada ou não, e o chamador não tem como saber. Se ele repete e o sistema grava de novo, um débito virou dois, o pior defeito num ledger: silencioso e com dinheiro do cliente. A solução também precisa vencer a corrida entre duas requisições com a mesma chave em réplicas diferentes da API.

## Decisão

O cabeçalho `Idempotency-Key` é obrigatório nas escritas de lançamento e de estorno, e sem ele a resposta é 400 `IDEMPOTENCY_KEY_REQUIRED`. O banco impõe a unicidade em `idempotency_keys`, com chave primária `(account_id, idempotency_key)`, então a mesma chave em contas diferentes não colide. A tabela guarda também o hash do pedido e o identificador do lançamento gerado.

O primeiro passo da transação de escrita ([documento de arquitetura 0005](0005-transacao-unica-com-outbox.md)) é um `INSERT ... ON CONFLICT DO NOTHING` da chave. Uma linha inserida significa pedido novo, e a transação segue. Nenhuma linha significa chave já usada, e a aplicação compara o hash: se for igual, devolve a resposta original, reconstruída do lançamento gravado, com o mesmo status e o cabeçalho `Idempotent-Replayed: true`; se for diferente, responde 422 `IDEMPOTENCY_KEY_REUSED` sem tocar em saldo algum. A corrida se resolve sozinha: em `READ COMMITTED`, o `INSERT` que esbarra numa linha ainda não confirmada espera a outra transação terminar, e depois cai no caminho de repetição (se ela confirmou) ou segue como novo (se deu rollback).

O hash é um SHA-256 sobre a forma canônica do comando, e não sobre os bytes crus, porque espaços ou ordem de campos diferentes numa repetição legítima não podem virar 422. A cadeia leva o `client_id` do token, de modo que dois sistemas com a mesma chave e o mesmo corpo deixam de ver a repetição um do outro ([documento de arquitetura 0023](0023-client-id-no-hash-do-pedido.md)). A cadeia exata e os vetores de teste estão em [Idempotência e hash canônico](../../05-contratos/idempotencia-e-hash-canonico.md).

Só o pedido bem-sucedido consome a chave, porque a linha de idempotência está na mesma transação do lançamento: uma recusa por saldo insuficiente faz rollback e não deixa rastro, e repetir depois de um crédito reavalia o pedido, que é o certo. As chaves ficam guardadas por 35 dias ([documento de arquitetura 0017](0017-retencao-de-35-dias-das-chaves-de-idempotencia.md)), e passada a janela uma chave repetida vira pedido novo.

## Alternativas descartadas

- Unicidade direto em `ledger_entries`. É a mais séria, porque não precisa de limpeza e nunca esquece, mas põe num índice permanente de 10 anos um dado que só importa por algumas semanas.
- `SETNX` no Redis com TTL. Segunda infraestrutura, fora da transação: se o Redis perde a chave, o lançamento duplica; se grava e a transação falha, recusa um pedido legítimo.
- Chave de negócio do chamador, como o identificador fim a fim do Pix. Nem todo chamador tem uma, e o ledger passaria a falar o vocabulário de cada um. O cabeçalho pode carregá-la quando existir.
- Chave sem hash. Um cliente com defeito que reutiliza a chave com outro valor receberia o lançamento antigo e acreditaria no novo. O hash troca um dano silencioso por um 422 barulhento.

## Consequências

Repetir uma requisição passa a ser sempre seguro, e o mau uso da chave aparece como erro. O custo é uma escrita e um índice único a mais por lançamento, uma tabela com retenção própria e a obrigação de cada cliente gerar chaves boas (a recomendação é UUID). Mudar a canonicalização exige versionar o hash (`hash_version`) e conviver com as duas formas durante a retenção. Os exemplos de resposta estão em [Fluxo: repetição idempotente](../../06-fluxos/repeticao-idempotente.md).
