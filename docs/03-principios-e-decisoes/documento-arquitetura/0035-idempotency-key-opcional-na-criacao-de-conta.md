# Documento de arquitetura 0035: `Idempotency-Key` opcional na criação de conta

## Contexto

A repetição interna da criação de conta, a que a unidade de trabalho faz quando a conexão cai no commit, está coberta pelo [documento de arquitetura 0031](0031-criacao-de-conta-repetivel-com-identificador-previo.md). A repetição do chamador não: quem leva um timeout ou um 503 e reenvia o pedido cria uma segunda conta, e nada no cadastro diz que as duas são o mesmo pedido. A rota é de apoio, mas a segunda conta responde `201` como a primeira, e ninguém percebe a duplicata na hora.

O corpo carrega um CPF ou CNPJ, e um SHA-256 simples de um documento de onze dígitos se resolve por força bruta em minutos a partir de um despejo do banco. Por isso `accounts` guarda um índice cego, um HMAC com a chave do cofre, e por isso o hash do pedido não pode ser um SHA-256 do corpo.

## Decisão

`POST /v1/accounts` aceita `Idempotency-Key` opcional. Sem o cabeçalho, a conta é criada sem reserva de chave. Com ele, a chave pertence ao chamador, o `client_id` do token: repetir o pedido com o mesmo corpo devolve a conta existente, com `201`, o mesmo `Location` e `Idempotent-Replayed: true`, e repetir a chave com outro corpo devolve 422 `IDEMPOTENCY_KEY_REUSED`. Cabeçalho vazio, repetido ou malformado é 400.

A reserva mora em `account_creation_keys`, com chave primária `(client_id, idempotency_key)`. Como a chave já carrega o chamador, o `client_id` não entra no hash, ao contrário dos lançamentos ([documento de arquitetura 0023](0023-client-id-no-hash-do-pedido.md)). A reserva é um `INSERT ... ON CONFLICT DO NOTHING` antes da inserção da conta, na mesma transação, com a chave estrangeira para `accounts` adiada até o commit, e duas requisições simultâneas com a mesma chave serializam no índice. A repetição não escreve nada.

O hash do pedido é o SHA-256 de uma forma canônica com a operação, a moeda, o limite e o índice cego do documento no lugar do documento. A repetição calcula o índice com cada versão de chave viva e aceita se alguma casa, então uma rotação de chaves não transforma uma repetição legítima em 422. A chave dura 35 dias, como as de lançamento, e a poda do Worker cobre as duas tabelas ([documento de arquitetura 0017](0017-retencao-de-35-dias-das-chaves-de-idempotencia.md)).

## Alternativas descartadas

- Reaproveitar `idempotency_keys`. A tabela exige `entry_id` e uma conta que já existe como parte da chave, e a criação não tem nenhum dos dois.
- Chave global, sem o chamador. Obrigaria o `client_id` a entrar no hash, e um chamador descobriria que um valor já foi usado por outro sistema pela diferença entre criar e receber 422.
- SHA-256 simples do corpo. Deixaria no banco um verificador de CPF que um despejo permite resolver por força bruta.
- HMAC com chave própria, sem relação com o índice cego. Resolveria a rotação sozinho, ao custo de um segredo novo para provisionar, girar e proteger, e o índice cego já existe e já está sob o cofre.
- Aceitar o identificador da conta do sistema de origem. Muda o contrato da resposta e pede um mapeamento, e não entra na primeira versão ([Questões em aberto](../questoes-em-aberto.md)).
- Manter o risco documentado. Aceitar a chave custa uma tabela e um caminho de leitura e fecha o caminho de duplicata por reenvio do cliente.

## Consequências

A rotação de chaves piora num ponto: a repetição só reconhece o pedido enquanto a versão de chave usada na criação continuar viva. Se alguém aposentar uma versão antes de passarem 35 dias da última criação feita com ela, uma repetição legítima volta 422 em vez da conta original. Falha para o lado seguro, porque nunca cria conta duplicada, e é um risco aceito. O procedimento de rotação espera os 35 dias antes de retirar uma versão ([Fluxo: rotação das chaves de dados pessoais](../../06-fluxos/rotacao-de-chaves.md)), e se a rotação precisar aposentar versões em menos tempo, o hash passa a ser um HMAC com chave própria.

Cada conta criada com chave custa mais uma linha em `account_creation_keys` até a poda, de tamanho parecido com o das linhas de `idempotency_keys` ([Capacidade e escala](../../08-resiliencia-e-operacao/capacidade-e-escala.md)).
