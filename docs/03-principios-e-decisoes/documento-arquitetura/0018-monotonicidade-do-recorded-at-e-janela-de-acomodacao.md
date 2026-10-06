# Documento de arquitetura 0018: Monotonicidade do `recorded_at` e janela de acomodação

## Contexto

O saldo em T é o `balance_after` do último lançamento com `recorded_at <= T` ([documento de arquitetura 0013](0013-tempo-do-saldo-registrado-em-vs-ocorrido-em.md)). Para isso valer, o `recorded_at` de uma conta precisa crescer junto com `account_version`, e serializar as escritas pela linha de `account_balances` não basta, por dois furos.

O primeiro é o relógio. A linha serializa a ordem das transações, não o que `clock_timestamp()` devolve. Um ajuste brusco de NTP, ou um failover para um nó com o relógio atrasado, faz a versão 8 receber um `recorded_at` anterior ao da versão 7, e uma consulta com T entre os dois devolveria um saldo que a conta nunca teve naquele instante. O segundo é a janela entre atribuir o `recorded_at` e confirmar a transação: para um T dentro dos últimos milissegundos pode haver uma transação em voo com `recorded_at <= T` que a consulta ainda não enxerga, e que a mesma consulta enxerga um instante depois. A repetibilidade do passado precisa de um prazo.

## Decisão

O banco impõe a monotonicidade no mesmo `UPDATE` que trava a linha. `account_balances.last_recorded_at` guarda o último instante da conta, e o `UPDATE` o atualiza com `GREATEST(clock_timestamp(), last_recorded_at + 1 microssegundo)`. O `recorded_at` do lançamento é o valor que esse `UPDATE` devolve, estritamente crescente por conta mesmo com o relógio recuando, e vale também para a primeira escrita depois de uma troca de primário, porque o valor mora na linha e não na memória.

Quando a correção acontece, o `UPDATE` devolve `recorded_at_corrected`, a escrita registra um aviso e incrementa `ledger_recorded_at_corrections_total`. A conferência de integridade continua verificando a regra por conta (`CHAIN_NON_MONOTONIC`), como segunda opinião. Corrigir é melhor que recusar, porque recusar derrubaria lançamentos válidos por um defeito de relógio que o chamador não causou.

A resposta do saldo histórico traz `settled`, verdadeiro quando `asOf <= database_now - janela`, com a janela de 5 segundos (`Ledger:Balance:SettlingWindowSeconds`). Fora da janela o valor é definitivo e repetível, e dentro dela o chamador sabe que ainda pode mudar. Os 5 segundos vêm do maior tempo que uma transação de escrita pode ficar aberta depois de receber o `recorded_at`, dado pelo `idle_in_transaction_session_timeout` de 5 s e pelo timeout de requisição de 3 s. É um valor conservador, e ninguém mediu ainda se dá para reduzi-lo ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md)). A promessa de repetibilidade vale "para instantes fora da janela de acomodação" ([Contrato da API REST](../../05-contratos/api-rest.md)).

## Alternativas descartadas

- Só a serialização pela linha. Não cobre relógio que recua, e a conferência detectaria o problema depois de a consulta já ter respondido errado.
- Recusar a escrita quando o relógio recuar. Troca um erro de saldo histórico por indisponibilidade de escrita que o chamador não consegue corrigir.
- Segurar a resposta até a janela passar. Um `asOf` recente esperaria 5 segundos e o p99 de 50 ms deixaria de existir. Um booleano custa quase nada.
- O instante do commit (`track_commit_timestamp`). Dá o instante real de visibilidade, mas não está no índice do saldo, exigiria uma junção por consulta, e a ordem de commit pode divergir da ordem de versão.
- Ordenar só por `account_version`. A versão é uma ordem total, mas a pergunta do produto é por instante, e traduzir instante em versão volta ao mesmo problema.

## Consequências

Quando o relógio recua, os lançamentos de uma conta saem com o instante do anterior mais 1 µs, à frente do relógio do banco pelo tamanho do recuo, até o relógio alcançá-los. Em troca, `last_recorded_at` é uma coluna a mais numa linha que já é atualizada a cada lançamento.

Os testes são `RecordedAtMonotonicityTests`, `SettledWindowTests`, `AsOfMatchesChainTests` e a verificação `CHAIN_NON_MONOTONIC`, em `IntegrityDetectsTamperingTests`.
