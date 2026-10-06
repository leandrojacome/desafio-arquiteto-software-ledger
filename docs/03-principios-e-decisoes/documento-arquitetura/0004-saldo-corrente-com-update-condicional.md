# Documento de arquitetura 0004: Saldo corrente com update condicional

## Contexto

Duas requisições podem chegar juntas para a mesma conta, e a meta pede até 50 por segundo nessa situação ([Requisitos não funcionais](../../02-contexto-e-requisitos/requisitos-nao-funcionais.md)). Não pode haver atualização perdida, em que as duas leem 100 e escrevem 90 e 80, nem conta que estoura o limite porque duas decisões olharam o mesmo saldo antigo.
## Decisão

O saldo atual vive em `account_balances`, uma linha por conta, e toda movimentação é uma única instrução. Na forma essencial:

```text
UPDATE account_balances
SET balance = balance + @delta, version = version + 1
WHERE account_id = @account_id AND balance + @delta >= -overdraft_limit
RETURNING balance, version;
```

O `@delta` é positivo no crédito e negativo no débito. Uma linha devolvida significa movimentação aceita, e `balance` e `version` alimentam o lançamento ([documento de arquitetura 0003](0003-saldo-apos-em-cada-lancamento.md)). Zero linhas significa conta inexistente, moeda diferente ou saldo insuficiente, e só nesse caminho uma segunda leitura distingue os casos. A instrução real, que também confere a moeda e grava o lançamento, está em [Fluxo: registro de lançamento](../../06-fluxos/registro-de-lancamento.md).

Em `READ COMMITTED`, o `UPDATE` pega o lock da linha e quem chega depois espera. Quando o primeiro confirma, o PostgreSQL reavalia o `WHERE` do segundo contra a versão confirmada, então a condição de saldo sempre vê o valor mais recente. A linha da conta é o ponto de serialização, sem lock explícito e sem deadlock entre contas, porque cada transação toca uma única linha de saldo. O `UPDATE` vem depois da reserva da chave e da validação, para o lock durar pouco ([documento de arquitetura 0005](0005-transacao-unica-com-outbox.md)), e um `lock_timeout` de 1 segundo limita a espera: estourar vira 503 com `Retry-After` ([documento de arquitetura 0011](0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md)). A restrição `ck_account_balances_balance_floor` fica na tabela como segunda trava.

## Alternativas descartadas

- `SELECT ... FOR UPDATE`, conta na aplicação e depois `UPDATE`. Serializa igual, com duas idas ao banco e o lock preso durante o tempo de rede e de CPU da aplicação.
- Concorrência otimista com `version` e nova tentativa. A 50 escritas por segundo na mesma conta quase toda tentativa colide, e cada colisão repete leitura, validação e hash.
- `SERIALIZABLE`. Paga o rastreio do SSI e exige nova tentativa no erro 40001, para proteger uma invariante que cabe numa linha.
- Advisory locks, fila por conta ou partição no broker. Entregam a ordem que o lock de linha já entrega, com mecanismo ou infraestrutura nova.

## Consequências

A invariante fica no banco: o `WHERE` decide a recusa, e o `CHECK` barra qualquer outro `UPDATE` que levasse o saldo abaixo do limite, inclusive um feito fora do repositório. O `ParallelDebitsTests` inclui o caso de 100 débitos de 10 contra saldo 500, que exige 50 aceitos, 50 recusados e saldo final zero, e o `ParallelDebitsHttpTests` repete esse cenário pela API.

O teto por conta é o tempo que a linha fica travada, do `UPDATE` ao commit, flush do WAL incluído. Com uns 6 ms, são cerca de 166 lançamentos por segundo, contra a meta de 50, e o commit síncrono no standby derruba esse teto: com 20 ms a mais, cai para uns 38. É conta de mesa, e a topologia com standby síncrono não é exercitada neste repositório ([Capacidade e escala](../../08-resiliencia-e-operacao/capacidade-e-escala.md) e [Limites conhecidos](../../09-qualidade/limites-conhecidos.md)). Transferência atômica entre contas não existe na primeira versão: o chamador faz um débito e um crédito idempotentes e compensa se um falhar.
