# Documento de arquitetura 0020: Identificadores UUID v7

## Contexto

Os identificadores do ledger nascem na aplicação, não no banco. O `entry_id` e o id do evento precisam existir antes do `INSERT`, porque a linha de idempotência aponta para o lançamento antes de ele ser gravado, e o `account_id` entra como dado autenticado na cifra do documento do titular ([Modelo de dados](../../05-contratos/modelo-de-dados.md)). Todas as tentativas de uma requisição reaproveitam o mesmo identificador, o que só é possível se ele for gerado fora da transação.

O formato pesa no índice. Um UUID v4 é aleatório, então cada inserção cai numa página qualquer da chave primária. Enquanto o índice cabe na memória isso é barato, mas `ledger_entries` só cresce e guarda dez anos de lançamentos: quando o índice deixa de caber, cada escrita vira leitura aleatória de disco, e as páginas se partem ao longo do tempo.

## Decisão

Os identificadores são UUID v7: 48 bits de milissegundos Unix no começo, versão e variante nos lugares do RFC 9562 e o resto aleatório. A porta `IIdGenerator`, em `Ledger.Application`, tem uma única implementação, `Uuid7IdGenerator`, em `Ledger.Infrastructure`, que chama `Guid.CreateVersion7` com o instante vindo do `TimeProvider`. Os testes fixam o relógio com `FakeTimeProvider` e conferem os bits do instante, a versão e a variante (`Uuid7IdGeneratorTests`). O banco guarda o valor como `uuid`.

Os casos de uso recebem o gerador por injeção, e o `BannedSymbols.txt` proíbe `Guid.NewGuid` nos projetos de produção, com a instrução de usar `IIdGenerator.NewId`.

O v7 só ordena identificadores gerados em milissegundos diferentes, e dentro do mesmo milissegundo a ordem entre dois deles não é garantida. Nada no sistema depende dela: a ordem dos lançamentos de uma conta vem de `account_version` ([documento de arquitetura 0003](0003-saldo-apos-em-cada-lancamento.md)), e o extrato pagina por posição, `(recorded_at, account_version)`, não por identificador ([documento de arquitetura 0025](0025-extrato-por-posicao-com-cursor-assinado.md)). O que o prefixo temporal entrega é localidade: as inserções caem perto da borda direita dos índices.

## Alternativas descartadas

- UUID v4, com `Guid.NewGuid`. Sem ordem temporal, espalha as inserções por todo o índice e fragmenta a chave primária.
- Identificador gerado pelo banco, como sequência ou função de UUID. O valor só seria conhecido depois do `INSERT`, e o desenho precisa dele antes. O PostgreSQL 16, além disso, não gera UUID v7.
- Escrever o algoritmo do v7 no projeto. O framework já o traz em `Guid.CreateVersion7`, e código próprio de geração de identificador seria só mais uma coisa para testar.

## Consequências

O identificador revela o milissegundo em que foi gerado, e quem tem o id lê dele o instante de criação do registro. Isso é propriedade do formato. O acesso ao ledger é decidido pelo token e pelo escopo, não por o identificador ser difícil de adivinhar.

O instante vem do relógio da instância da aplicação, não do banco. Um relógio atrasado deixa o identificador com prefixo antigo, o que piora só a localidade no índice e não a correção: a unicidade vem dos bits aleatórios, e as restrições de chave primária continuam valendo.
