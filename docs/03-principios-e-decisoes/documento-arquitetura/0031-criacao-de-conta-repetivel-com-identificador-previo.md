# Documento de arquitetura 0031: Criação de conta repetível, com identificador gerado antes

## Contexto

Quando o desfecho de um commit é desconhecido (a conexão cai depois de o servidor confirmar e antes de o cliente saber), a unidade de trabalho repete a função inteira. Se essa repetição gerasse uma segunda conta, a queda de uma conexão viraria duas contas para um pedido só. A rota aceita `Idempotency-Key` opcional ([documento de arquitetura 0035](0035-idempotency-key-opcional-na-criacao-de-conta.md)), mas essa chave protege o reenvio do cliente. A repetição interna precisa funcionar também sem ela.

Dois fatos do desenho definem a solução. O documento do titular é cifrado com o identificador da conta como dado associado, então o identificador existe antes da cifra, e a cifra roda fora da transação ([documento de arquitetura 0005](0005-transacao-unica-com-outbox.md)). E uma inserção que viola a chave primária aborta a transação no PostgreSQL: o comando seguinte responde `current transaction is aborted`, e a repetição não consegue se recuperar dentro dela.

## Decisão

O `CreateAccountHandler` gera o `accountId` e cifra o documento antes de abrir a transação. Dentro dela, uma instrução só insere a conta e a linha de saldo, com `ON CONFLICT (id) DO NOTHING` na inserção de `accounts`, e a linha `account.created` da trilha é gravada na mesma transação. Se a instrução devolve o `created_at`, a conta é nova.

Se devolve zero linhas, o identificador já existe. Sem `Idempotency-Key`, na primeira tentativa isso é um defeito (o gerador entregou um identificador repetido) e vira exceção. Da segunda em diante significa que a tentativa anterior confirmou: o handler lê o `created_at` gravado e devolve a mesma resposta, inclusive o `Location`, com o desfecho `already_created` no span e sem gravar nada de novo. O chamador não percebe a repetição.

## Alternativas descartadas

- `Idempotency-Key` obrigatória na criação. A rota é de apoio e administrativa, o hash do corpo teria de ser um HMAC porque o corpo contém o documento, e a repetição interna continuaria sem cobertura. A chave opcional do documento de arquitetura 0035 trata o reenvio do cliente.
- Desligar a repetição interna só na criação. Uma queda de conexão que a repetição resolveria viraria um 503.
- Gerar o identificador no banco. O `account_id` entra como dado autenticado na cifra, que acontece antes.
- Inserção simples, sem `ON CONFLICT`. A violação da chave primária deixa a transação abortada e a repetição não prossegue.

## Consequências

Uma queda de conexão no commit não cria conta nem linha de auditoria duplicada, e o teste `LostAnswerOfTheCommitOnAccountCreation_StillCreatesExactlyOneAccountAndOneAuditRow` cobre esse caso. O custo é uma ramificação no handler, a que lê o `created_at` de volta, e a regra de que o gerador de identificadores nunca devolve o mesmo valor duas vezes, o que o UUID versão 7 gerado pela aplicação respeita ([documento de arquitetura 0020](0020-identificadores-uuid-v7.md)).

Um reenvio do cliente sem `Idempotency-Key` gera outro `accountId` e cria outra conta. Se a criação de conta virar rota de uso comum, a chave deixa de poder ser opcional, como nas escritas.
