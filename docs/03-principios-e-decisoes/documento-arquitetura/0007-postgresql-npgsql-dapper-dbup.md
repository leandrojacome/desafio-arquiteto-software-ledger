# Documento de arquitetura 0007: PostgreSQL, Npgsql, Dapper e DbUp

## Contexto

O ledger depende de três coisas do banco: transações ACID, lock de linha com reavaliação do `WHERE` depois da espera ([documento de arquitetura 0004](0004-saldo-corrente-com-update-condicional.md)) e índices que sirvam uma busca pontual no tempo ([documento de arquitetura 0003](0003-saldo-apos-em-cada-lancamento.md)). Precisa ainda de `jsonb` para o outbox, de índices parciais para idempotência e estorno e de testes que provem tudo isso em qualquer máquina. Como a concorrência mora no SQL, o SQL das partes críticas tem que estar à vista.

## Decisão

PostgreSQL 16 é a única fonte de verdade. O acesso usa Npgsql com uma fonte de dados por tipo de trabalho (escrita, saldo, extrato, Worker e migrador), cada uma com o próprio pool, e a soma dos pools de todas as réplicas fica abaixo de `max_connections` menos uma reserva de manutenção. O Dapper só mapeia: o SQL é sempre explícito, e todo método recebe `CancellationToken`, para que os timeouts do [documento de arquitetura 0011](0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md) cheguem ao driver. Dinheiro é `numeric` no banco e `decimal` no código. As colunas `timestamptz` só recebem UTC, e o leitor da API converte o deslocamento de entrada antes de o instante chegar ao Npgsql ([documento de arquitetura 0036](0036-politica-de-fusos-horarios.md)).

As migrações são scripts SQL numerados, embutidos no assembly e executados pelo DbUp, que registra o que já rodou. Só andam para frente: erro se corrige com outro script, e uma mudança destrutiva se faz em duas versões, adicionar e migrar numa, remover na seguinte. O `MigrationChecksumTests` reprova um script alterado depois de registrado. Como o DbUp não tem trava própria, a execução usa um advisory lock com prazo (`Migrations:LockTimeoutSeconds`): duas réplicas subindo juntas não correm uma contra a outra nem esperam para sempre por uma sessão esquecida.

Para RPO zero e RTO de 15 minutos, a topologia de produção desenhada usa um standby com replicação síncrona e failover automático. É hipótese: o repositório não a exercita ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md) e [Implantação: topologia de produção](../../04-modelos-c4/implantacao-producao.md)). O `docker compose` local usa uma instância só.

## Alternativas descartadas

- EF Core. Serviria para o cadastro de contas e atrapalharia o resto: o `UPDATE ... RETURNING` condicional, o `INSERT ... ON CONFLICT` e o `FOR UPDATE SKIP LOCKED` do outbox não têm expressão natural em LINQ e acabariam em SQL cru, e o rastreador de mudanças esconde o número de idas ao banco e a ordem das instruções. Com poucas tabelas, dois estilos de acesso custam mais que um.
- Outros bancos. SQL Server e MySQL não dariam nada a mais. CockroachDB ou Spanner dão serializabilidade distribuída, mas o consenso na escrita ameaça os 150 ms de p99 e custa caro para um problema de uma região. DynamoDB faria bem a escrita condicional, mas a consulta de saldo em um instante e a auditoria ad hoc ficam mais difíceis.
- Migrações por EF, FluentMigrator ou Flyway. As do EF só fazem sentido com EF, o FluentMigrator esconde o SQL numa DSL que ninguém revisa com atenção, e o Flyway pede JVM no pipeline.
- ADO.NET puro, sem Dapper. Funciona, com mais código de mapeamento e nenhuma segurança a mais.

## Consequências

O SQL de concorrência fica sob controle total, a revisão de migração é revisão de código e cada ida ao banco é visível. Em troca, o SQL não é verificado na compilação: uma coluna renomeada só quebra em execução, e a defesa é teste de integração contra PostgreSQL real, sem fakes em memória. O mapeamento é manual. E replicação síncrona quer dizer que, se o standby cair, as escritas esperam ou o failover é acionado, o preço de RPO zero que a decisão aceita pagar.
