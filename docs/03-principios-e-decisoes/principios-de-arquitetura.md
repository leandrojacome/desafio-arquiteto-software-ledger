# Princípios de arquitetura

Seis princípios orientam o desenho do ledger. Cada um vem com o motivo, o mecanismo que o impõe e os testes que o cobrem. Os cinco primeiros descrevem o comportamento do sistema, e o último, o tamanho do desenho. As convenções de código estão em [Convenções de código](../12-engenharia/convencoes-de-codigo.md), e as decisões que realizam cada princípio, no [índice de documentos de arquitetura](documento-arquitetura/README.md).

## 1. O ledger é imutável

Um lançamento só é inserido. Não existe `UPDATE` nem `DELETE` sobre `ledger_entries`, e nenhum lançamento sai fisicamente do banco durante os dez anos de retenção. Erro se corrige com estorno, um novo lançamento cujo `reverses_entry_id` aponta para o original. Um livro-razão que pode ser reescrito deixa de provar o que aconteceu, e auditoria, conciliação e confiança no saldo dependem de o passado não mudar.

A imutabilidade vale no banco, não só no código. Os gatilhos `tr_ledger_entries_forbid_update_delete` e `tr_ledger_entries_forbid_truncate` (e os de `audit_log`) nascem nas migrações das tabelas, a API só insere e lê em `ledger_entries`, e o Worker só lê. O tipo `Entry` não tem operação de alteração, e o índice único parcial `uq_ledger_entries_reverses_entry_id` garante um estorno por lançamento. Testes: `MigrationTests`, `LedgerEntriesAreNeverMutatedTests` e `ReverseEntryTests`. Decisão: [documento de arquitetura 0002](documento-arquitetura/0002-ledger-imutavel-somente-insercao.md).

## 2. A consistência do saldo vem antes de tudo

O `balance_after` do lançamento mais recente é igual ao saldo em `account_balances`, e esse saldo nunca fica abaixo de `-overdraft_limit`. Inserir o lançamento, atualizar o saldo e gravar o evento em `outbox_messages` acontecem na mesma transação, e quem decide se um débito passa é o banco, num `UPDATE` condicional, não uma leitura seguida de escrita no código. Se algo compete com isso, a operação é recusada em vez de arriscar o saldo. Um desvio entre saldo e lançamentos é incidente, e a conferência periódica do Worker existe para achá-lo.

O `UPDATE` de `ApplyEntrySql` só afeta a linha da conta se `ab.balance + @delta >= -ab.overdraft_limit`, e o mesmo comando devolve saldo, versão e `recorded_at` usados no lançamento. A restrição `ck_account_balances_balance_floor` repete a regra no banco, `uq_ledger_entries_account_id_account_version` impede duas escritas com a mesma versão, `PostgresUnitOfWork` abre uma transação por operação e `EntryWriteFlow` define a ordem dos passos. `ParallelDebitsTests`, `ParallelEntriesPreserveSumTests` e `ManyAccountsTests` atacam a regra com escritas concorrentes, `WriteFailureTests` e `CommitCuttingProxyTests` interrompem a transação em pontos escolhidos, e `IntegrityDetectsTamperingTests` mostra que a conferência enxerga adulteração. Decisões: [documento de arquitetura 0003](documento-arquitetura/0003-saldo-apos-em-cada-lancamento.md), [documento de arquitetura 0004](documento-arquitetura/0004-saldo-corrente-com-update-condicional.md), [documento de arquitetura 0005](documento-arquitetura/0005-transacao-unica-com-outbox.md) e [documento de arquitetura 0018](documento-arquitetura/0018-monotonicidade-do-recorded-at-e-janela-de-acomodacao.md).

## 3. Idempotência é obrigatória

Toda escrita que altera saldo exige `Idempotency-Key`, com unicidade `(conta, chave)` garantida pelo banco e comparação do hash do pedido. A mesma chave com o mesmo conteúdo devolve o resultado original, e com conteúdo diferente devolve 422. O estorno segue a mesma regra, e os consumidores de eventos deduplicam pelo `message_id`, porque a entrega é pelo menos uma vez. A retentativa automática só existe onde a operação é idempotente. Num sistema distribuído a repetição de uma chamada é rotina, e sem idempotência cada timeout vira uma chance de debitar duas vezes.

A chave primária `(account_id, idempotency_key)` de `idempotency_keys` é reservada na transação do lançamento por `ReserveKeySql`, o `CanonicalRequestHash` inclui o chamador e o tipo `IdempotencyKey` recusa chave vazia, longa ou com caractere invisível. A retentativa da escrita refaz a transação inteira, reserva da chave incluída (`WriteRetryPipeline`), e por isso é segura. A criação de conta tem tabela própria, `account_creation_keys`, porque a conta ainda não existe para ancorar a chave. Testes: `IdempotentReplayTests`, `CanonicalRequestHashTests` e `EventDeduplicationTests`. Decisões: [documento de arquitetura 0006](documento-arquitetura/0006-idempotencia-por-chave-e-hash.md), [documento de arquitetura 0017](documento-arquitetura/0017-retencao-de-35-dias-das-chaves-de-idempotencia.md) e [documento de arquitetura 0023](documento-arquitetura/0023-client-id-no-hash-do-pedido.md).

## 4. Falhar de forma segura

Diante de dúvida, o sistema recusa. Uma operação termina inteira ou não deixa rastro. Toda chamada para fora do processo (banco, broker) tem prazo, e a queda do broker não derruba a escrita, porque a caixa de saída absorve a publicação. Falha de infraestrutura devolve 503 com `Retry-After`, e a resposta nunca vaza pilha, texto de SQL ou nome de servidor. Os padrões são fechados: toda rota declara a política de autorização, a política de reserva exige chamador autenticado, e configuração ausente ou inválida impede a subida em vez de assumir um valor.

Os prazos de conexão, de comando e de instrução ficam em `Postgres:Sources`, o da requisição em `Resilience:RequestTimeoutSeconds`, o da confirmação do broker em `Outbox:ConfirmTimeoutSeconds` e o circuit breaker em `Resilience:BrokerCircuitBreaker`. O `GlobalExceptionHandler` traduz falha transitória em 503 e o resto em 500 sem detalhe, e os validadores de opções terminam o processo na subida, com código 3 e uma linha que nomeia a chave inválida. `EndpointPolicyCoverageTests` falha se uma rota ficar sem política, `ProblemDetailsTests` e `LogLeakTests` conferem o que resposta e log não contêm, `PostgresOutageE2ETests` e `BrokerOutageE2ETests` param os contêineres de verdade, e `ApiStartupTests` cobre a recusa de configuração inválida. Decisões: [documento de arquitetura 0005](documento-arquitetura/0005-transacao-unica-com-outbox.md), [documento de arquitetura 0011](documento-arquitetura/0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md) e [documento de arquitetura 0019](documento-arquitetura/0019-readiness-nao-depende-do-provedor-de-chaves.md).

## 5. Segurança e privacidade por padrão

Toda rota, exceto as verificações de saúde e o documento OpenAPI fora de produção, exige JWT com o escopo certo (`ledger.read` ou `ledger.write`). O documento do titular é cifrado com AES-256-GCM e só se busca por índice cego. Dado pessoal não aparece em log, métrica, trace nem mensagem de erro, segredo não entra no repositório, todo SQL usa parâmetros e o papel da aplicação tem o menor privilégio com que consegue trabalhar. A subida recusa configuração sem TLS com o banco e com o broker fora de `Development` e `Testing`. O que ficou sem exercício é a conexão TLS em si: nenhum teste abre uma, e o Compose local roda sem TLS ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)). O caminho padrão é o seguro, para que a segurança não dependa de alguém lembrar de ligar um recurso.

As políticas `ledger.read`, `ledger.write` e `AccountProvisioning` estão em `AuthorizationPolicies`, o `HolderDocumentProtector` usa `AesGcmDocumentCipher` e `HmacBlindIndex`, e o Serilog mascara valores sensíveis. Os validadores de produção recusam TLS desligado, provedor de chaves lido da configuração e curinga na lista de quem cria conta, as primitivas de criptografia só existem na pasta de segurança da infraestrutura e a trilha `audit_log` tem catálogo fechado. Testes: `RouteScopeMatrixTests`, `AccountDocumentProtectionTests`, `LogLeakTests`, `CryptographyBoundaryTests`, `RolePrivilegesTests` e `ProductionGuardsTests`. Decisões: [documento de arquitetura 0010](documento-arquitetura/0010-seguranca-jwt-e-criptografia-de-pii.md) e [documento de arquitetura 0028](documento-arquitetura/0028-trilha-de-auditoria-com-catalogo-fechado.md).

## 6. Simplicidade

Nenhuma tecnologia, camada, abstração ou serviço entra sem resolver um problema real de hoje. Monolito modular com dois executáveis, sem cache, sem EF Core e sem microsserviços são escolhas desta versão, cada uma com seu documento de arquitetura, e o que ficou de fora está em [Evolução futura](../11-evolucao/evolucao-futura.md), com o critério que justificaria a entrada. Uma abstração só nasce na terceira repetição ou quando uma fronteira de camada exige uma porta, porque antes disso duplicar custa menos do que abstrair.

São cinco projetos de código, com a dependência apontando só para dentro: Api e Worker enxergam Infrastructure, que enxerga Application, que enxerga Domain. As portas ficam em `Ledger.Application`, os casos de uso são classes `Handler` seladas com um único método público, e a infraestrutura expõe só o que os executáveis precisam. Testes: `LayerDependencyTests` e `InfrastructurePublicSurfaceTests`. Decisões: [documento de arquitetura 0001](documento-arquitetura/0001-monolito-modular-dois-executaveis.md), [documento de arquitetura 0007](documento-arquitetura/0007-postgresql-npgsql-dapper-dbup.md) e [documento de arquitetura 0009](documento-arquitetura/0009-sem-cache-na-v1.md).

## Quando dois atributos competem

A ordem de prioridade é consistência e durabilidade, segurança, disponibilidade, desempenho, observabilidade, escalabilidade, testabilidade e manutenibilidade. Ela só pesa quando há conflito, e os conflitos reais são poucos.

- **Consistência e disponibilidade.** Se não dá para garantir que a escrita é durável, ela é recusada com 503, em vez de aceita e reconciliada depois. Isso custa disponibilidade, de propósito.
- **Segurança e disponibilidade.** Se a chave de cifragem some, o sistema não cria conta nem decifra documento, e também não grava nada em claro. O dinheiro continua andando, porque lançamento e saldo nunca tocam a chave.
- **Desempenho e observabilidade.** O custo dos traces é controlado pela amostragem configurável do OpenTelemetry. O erro não some, porque o log de erro não é amostrado e carrega o `TraceId`.
- **Escalabilidade e manutenibilidade.** Prefere-se escalar uma base de código só, pagando com um banco único, a dividir em serviços e pagar com consistência distribuída.

## Leia também

- [Riscos e alternativas rejeitadas](riscos-e-trade-offs.md): o que foi descartado em nome destes princípios.
- [Modelo de consistência](../07-consistencia-e-seguranca/modelo-de-consistencia.md): as invariantes dos princípios 1 e 2 e quem as impõe.
- [Políticas de resiliência](../08-resiliencia-e-operacao/politicas-de-resiliencia.md): os prazos e as retentativas do princípio 4.
