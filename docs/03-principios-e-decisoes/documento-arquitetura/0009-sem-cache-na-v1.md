# Documento de arquitetura 0009: Sem cache na primeira versão

## Contexto

A meta de leitura é de 10.000 consultas de saldo por segundo, com p99 de até 50 ms ([Requisitos não funcionais](../../02-contexto-e-requisitos/requisitos-nao-funcionais.md)). Um número desses empurra a equipe para o Redis antes de qualquer medida, e vale resistir, porque as duas consultas do sistema são baratas por construção. O saldo atual é uma leitura por chave primária em `account_balances`. O saldo em um instante é o `balance_after` do último lançamento até T, uma busca em índice sem somar histórico ([documento de arquitetura 0003](0003-saldo-apos-em-cada-lancamento.md)), e com o `balance_after` como coluna incluída o PostgreSQL responde só pelo índice.

Cache de saldo falha em silêncio. O saldo muda a cada lançamento e a invalidação não participa da transação do banco: o commit acontece, a invalidação falha e o cliente vê um valor velho logo depois de um Pix. A escrita nunca consultaria o cache, porque a regra de saldo suficiente vive no `UPDATE` condicional ([documento de arquitetura 0004](0004-saldo-corrente-com-update-condicional.md)), então ele serviria só às leituras, e é nelas que um número desatualizado vira chamado de suporte ou decisão errada de outro sistema.

## Decisão

A primeira versão não tem cache de saldo em camada nenhuma: nem Redis, nem memória do processo, nem cache HTTP. As respostas de saldo e de extrato saem com `Cache-Control: no-store`. O saldo atual é lido direto de `account_balances`, e o saldo com `?asOf=` é resolvido pelo índice de `ledger_entries`.

Se o teste de carga com as 10.000 leituras misturadas às 2.000 escritas mostrar p99 do saldo acima de 50 ms de forma sustentada, ou leitura consumindo CPU e conexões do primário a ponto de ameaçar a escrita, a decisão deve ser revista. Esse teste ainda não foi feito na escala das metas ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md)). Se chegar a hora, o desenho previsto é cache-aside no Redis só para o saldo atual, com a `version` dentro do valor, TTL de poucos segundos e uso proibido na decisão de débito. As consultas `asOf` bem no passado são as candidatas mais seguras a cache longo, porque o ledger só cresce e o `recorded_at` não regride ([documento de arquitetura 0018](0018-monotonicidade-do-recorded-at-e-janela-de-acomodacao.md)). Os critérios de entrada estão em [Evolução futura](../../11-evolucao/evolucao-futura.md).

## Alternativas descartadas

- Redis em cache-aside para o saldo atual. Troca uma latência que ainda não é problema por um problema de consistência que existiria desde o primeiro dia. Continua como evolução.
- Write-through. Exige gravar em dois sistemas sem transação comum, e qualquer falha entre os dois deixa o cache errado até o TTL.
- Cache em memória por instância. Dois chamadores consultando em sequência poderiam ver saldos diferentes, o que num ledger é pior que lento.
- Cache HTTP com ETag. Os chamadores são sistemas internos, não navegadores, e a ETag derivada de `version` ainda exigiria ler a linha da conta.
- Réplica de leitura. Resolve capacidade, não a latência de uma busca por chave, e traz atraso de replicação, a mesma classe de inconsistência de um cache.
- Snapshots periódicos. Resolvem um problema que o `balance_after` já eliminou.

## Consequências

Sem cache não há invalidação nem segunda fonte de verdade, e a leitura sempre reflete o último commit.

O preço é que toda leitura vai ao primário, e um pico de consultas compete com as escritas por conexões e CPU no mesmo PostgreSQL. A mitigação são pools separados e limites por tipo de operação ([documento de arquitetura 0011](0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md)). Também se assume que 10.000 leituras por segundo cabem na instância escolhida, uma estimativa de ordem de grandeza que o teste de carga com k6 ([Estratégia de testes](../../09-qualidade/estrategia-de-testes.md)) deve confirmar ou derrubar.
