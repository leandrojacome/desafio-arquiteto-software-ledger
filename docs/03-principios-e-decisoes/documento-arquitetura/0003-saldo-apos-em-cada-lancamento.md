# Documento de arquitetura 0003: Saldo após em cada lançamento

## Contexto

A pergunta central do produto é quanto havia na conta no instante T, e T pode ser agora ou dez anos atrás, com p99 de 50 ms a 10.000 consultas por segundo ([Requisitos não funcionais](../../02-contexto-e-requisitos/requisitos-nao-funcionais.md)). As contas mais antigas e movimentadas acumulam muitas linhas, então qualquer abordagem cujo custo cresce com o histórico fica mais lenta justamente onde mais dói.

## Decisão

Cada linha de `ledger_entries` guarda `balance_after`, o saldo da conta imediatamente depois dela. O valor vem do `RETURNING` do `UPDATE` condicional em `account_balances` ([documento de arquitetura 0004](0004-saldo-corrente-com-update-condicional.md)), dentro da transação do lançamento, nunca de uma leitura anterior. Junto vai `account_version`, devolvida pelo mesmo `UPDATE`, uma ordem total por conta que não depende do relógio.

O saldo em T é o `balance_after` do último lançamento da conta com `recorded_at <= T`, ordenado por `recorded_at` e `account_version` decrescentes, com `LIMIT 1`. O índice `ix_ledger_entries_account_id_recorded_at_account_version`, que inclui `balance_after`, atende a consulta por index-only scan, e sem lançamento até T o saldo é zero. O saldo atual vem de `account_balances` por chave primária. A instrução completa está em [Fluxo: consulta de saldo em um instante](../../06-fluxos/consulta-em-um-instante.md).

O `recorded_at` sai do mesmo `UPDATE` do saldo, calculado com `clock_timestamp()` e não com `now()`, que devolve o início da transação e deixaria duas transações concorrentes gravarem horários em ordem inversa à da cadeia. O mesmo `UPDATE` faz o valor crescer a cada lançamento da conta, mesmo com o relógio recuando ([documento de arquitetura 0018](0018-monotonicidade-do-recorded-at-e-janela-de-acomodacao.md)). Por que `recorded_at` vale mais que `occurred_at` está no [documento de arquitetura 0013](0013-tempo-do-saldo-registrado-em-vs-ocorrido-em.md). A cadeia tem uma invariante simples, o `balance_after` de cada lançamento é o do anterior mais o valor com sinal, e o Worker a confere periodicamente.

## Alternativas descartadas

- Somar o histórico na hora. O custo cresce com o número de lançamentos até T, e pela estimativa de mesa uma conta com 500 mil lançamentos já pede dezenas a centenas de milissegundos.
- Snapshots periódicos mais a soma do que veio depois. Resolve a escala, mas cria uma parte móvel inteira (job, reprocessamento, regra para o dia corrente) e fica como plano B em [Evolução futura](../../11-evolucao/evolucao-futura.md).
- View materializada ou `SUM() OVER` na leitura. Mesmo custo da soma, mais um refresh no caminho.
- Só `account_balances`, que responde o presente e não o passado, ou event sourcing com projeções temporais, que dá o mesmo resultado com muito mais operação.

## Consequências

A leitura custa uma descida de árvore B e uma linha, igual para ontem e para cinco anos atrás, e o extrato mostra saldo corrente sem cálculo.

O preço: o dado é redundante, e um `balance_after` gravado errado passaria em silêncio se ninguém conferisse, por isso a conferência faz parte da solução. Cada linha cresce algumas dezenas de bytes e o índice é mais um para manter. Não existe lançamento retroativo: a ordem da cadeia é a da gravação, e um relógio do primário que recue é corrigido no próprio `UPDATE` e registrado como aviso.
