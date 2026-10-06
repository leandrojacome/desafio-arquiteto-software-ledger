# Documento de arquitetura 0013: Tempo do saldo

## Contexto

Todo lançamento carrega dois instantes. `recorded_at` é quando o ledger gravou, pelo relógio do primário. `occurred_at` é a data de negócio informada pelo chamador: a compra no cartão foi na sexta às 23h50, mas o arquivo de liquidação só chegou na segunda. "Qual era o saldo em T?" tem respostas diferentes conforme o eixo de tempo, e a escolha decide se o passado pode mudar.

## Decisão

O saldo em T é o `balance_after` do último lançamento da conta com `recorded_at <= T`, desempatando por `account_version` ([documento de arquitetura 0003](0003-saldo-apos-em-cada-lancamento.md)). Sem lançamento até T, o saldo é zero. O `occurred_at` é guardado e devolvido, aceito até 5 minutos à frente do relógio da API (se omitido, vale o instante do registro), e nunca entra em cálculo de saldo. O extrato filtra e ordena por `recorded_at`, o mesmo eixo, para que o saldo em T2 menos o saldo em T1 bata com a soma dos lançamentos do intervalo.

O `recorded_at` vem de `clock_timestamp()`, avaliado dentro da transação depois de o `UPDATE` do saldo travar a linha da conta. As escritas de uma conta são serializadas por essa linha, e o mesmo `UPDATE` impede que o instante regrida dentro dela. É isso que torna o passado imutável: uma consulta com T no passado devolve sempre o mesmo valor. Como o banco impõe a monotonicidade, e a partir de quando o valor é definitivo, está no [documento de arquitetura 0018](0018-monotonicidade-do-recorded-at-e-janela-de-acomodacao.md).

A razão técnica é o `balance_after`, um saldo corrido que só faz sentido na ordem em que os lançamentos entraram. Se o saldo seguisse o `occurred_at`, um lançamento retroativo entraria no meio da sequência e mudaria o `balance_after` de todos os seguintes: seria reescrever linhas num ledger só de inserção ([documento de arquitetura 0002](0002-ledger-imutavel-somente-insercao.md)) e alterar saldos já informados e extratos já emitidos.

## Alternativas descartadas

- Saldo por `occurred_at`. O passado muda e o `balance_after` perde o sentido.
- Modelo bitemporal. Responde "qual era o saldo no dia D, pelo que se sabe hoje" e "pelo que se sabia na data X", e resolve o retroativo sem reescrever nada. Mas o saldo corrido deixa de ser coluna e vira cálculo (perde a busca em índice ou exige snapshots por eixo), a regra de saldo mínimo vira retroativa (um lançamento antigo pode negativar dias já fechados) e o modelo fica bem mais difícil de entender. Nenhum requisito pede retroatividade.
- Relógio de cada instância da API. Uma diferença de milissegundos entre instâncias bastaria para o `recorded_at` regredir dentro da conta.
- Aceitar o `occurred_at` como `recorded_at`. Entregaria ao chamador o controle do passado do ledger.

## Consequências

Quem precisa de efeito retroativo não o obtém: lança agora, informando o `occurred_at` real, e a conciliação cruza por `occurred_at` sem que ele defina saldo.

A ordem dos lançamentos de uma conta não depende do relógio, porque o `UPDATE` corrige o instante que recuaria. O que o relógio ainda afeta é a proximidade do `recorded_at` com o tempo real: depois de um failover para um nó atrasado, os lançamentos seguintes saem à frente do relógio do banco até ele alcançar. Por isso o ajuste de relógio deve ser gradual e o desvio, monitorado.

Se surgir requisito de efeito retroativo no saldo, como um arquivo de liquidação que deva valer no dia anterior para cálculo de juros, minha recomendação é tratar competência como contabilidade, no razão geral, e não no ledger de contas ([Questões em aberto](../questoes-em-aberto.md)). Se o requisito vier mesmo assim, o caminho é uma projeção bitemporal alimentada pelos eventos do outbox, em tabela própria ([Evolução futura](../../11-evolucao/evolucao-futura.md)).
