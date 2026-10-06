# Rastreabilidade de requisitos

Cada requisito funcional tem ao menos um teste automatizado (NFR-15, em [Requisitos não funcionais](requisitos-nao-funcionais.md)), e esta página é o mapa: uma linha por par de requisito e teste, com o nome da classe (e do método, quando um só método carrega o requisito) e o projeto onde ela mora. Os requisitos estão em [Requisitos funcionais](requisitos-funcionais.md).

## Como a matriz é lida

O `RequirementsMatrixTests`, do projeto `Ledger.Architecture.Tests`, lê os `FR-nn` que abrem cada requisito em [Requisitos funcionais](requisitos-funcionais.md) (`**FR-nn`) e as linhas da tabela abaixo, cada uma com três células: o requisito, o teste entre crases (`Classe` ou `Classe.Metodo`) e a pasta do projeto em `tests/`. A suíte falha com requisito sem linha, linha que cita requisito inexistente, linha cuja classe ou método não existe mais nos fontes do projeto indicado e linha repetida. A mesma classe confere que o README da raiz, o de `docker` e todas as páginas de `docs` só citam classes `*Tests` que existam em `tests/`.

## Matriz

| Requisito | Teste | Projeto |
|---|---|---|
| FR-01 | `CreateAccountTests` | Ledger.Api.IntegrationTests |
| FR-01 | `CreateAccountValidationTests` | Ledger.Api.IntegrationTests |
| FR-01 | `CreateAccountIdempotencyTests` | Ledger.Api.IntegrationTests |
| FR-01 | `AccountProvisioningTests` | Ledger.Api.IntegrationTests |
| FR-01 | `AccountDocumentProtectionTests` | Ledger.Api.IntegrationTests |
| FR-01 | `AccountsE2ETests` | Ledger.EndToEnd.Tests |
| FR-02 | `RegisterEntryTests` | Ledger.Api.IntegrationTests |
| FR-02 | `EntriesE2ETests` | Ledger.EndToEnd.Tests |
| FR-03 | `RegisterEntryTests` | Ledger.Api.IntegrationTests |
| FR-03 | `ParallelDebitsTests` | Ledger.Api.IntegrationTests |
| FR-04 | `RejectedEntryTests` | Ledger.Api.IntegrationTests |
| FR-04 | `EntriesRulesE2ETests` | Ledger.EndToEnd.Tests |
| FR-05 | `EntryValidationTests` | Ledger.Api.IntegrationTests |
| FR-05 | `RegisterEntryRequestReaderTests` | Ledger.Api.IntegrationTests |
| FR-05 | `CreateAccountValidationTests` | Ledger.Api.IntegrationTests |
| FR-05 | `RejectedEntryTests` | Ledger.Api.IntegrationTests |
| FR-05 | `EntryHttpSurfaceTests` | Ledger.Api.IntegrationTests |
| FR-05 | `EntryInstantReadingTests` | Ledger.Api.IntegrationTests |
| FR-05 | `EntryTimeZoneTests` | Ledger.Api.IntegrationTests |
| FR-05 | `NpgsqlInfinityConversionTests` | Ledger.Api.IntegrationTests |
| FR-06 | `EntryValidationTests` | Ledger.Api.IntegrationTests |
| FR-06 | `EntryRequestOrderTests` | Ledger.Api.IntegrationTests |
| FR-07 | `IdempotentReplayTests` | Ledger.Api.IntegrationTests |
| FR-07 | `SameIdempotencyKeyTests` | Ledger.Api.IntegrationTests |
| FR-07 | `SameIdempotencyKeyHttpTests` | Ledger.Api.IntegrationTests |
| FR-07 | `EntryTimeZoneTests` | Ledger.Api.IntegrationTests |
| FR-08 | `IdempotentReplayTests` | Ledger.Api.IntegrationTests |
| FR-08 | `PostgresIdempotencyStoreTests` | Ledger.Api.IntegrationTests |
| FR-08 | `EntryTimeZoneTests` | Ledger.Api.IntegrationTests |
| FR-09 | `ParallelDebitsTests` | Ledger.Api.IntegrationTests |
| FR-09 | `ParallelDebitsHttpTests` | Ledger.Api.IntegrationTests |
| FR-09 | `ParallelEntriesPreserveSumTests` | Ledger.Api.IntegrationTests |
| FR-09 | `ManyAccountsTests` | Ledger.Api.IntegrationTests |
| FR-10 | `ReverseEntryTests` | Ledger.Api.IntegrationTests |
| FR-11 | `ReverseEntryTests` | Ledger.Api.IntegrationTests |
| FR-11 | `ParallelReversalsTests` | Ledger.Api.IntegrationTests |
| FR-11 | `ParallelReversalsHttpTests` | Ledger.Api.IntegrationTests |
| FR-12 | `ReverseEntryTests` | Ledger.Api.IntegrationTests |
| FR-13 | `LedgerEntriesAreNeverMutatedTests` | Ledger.Architecture.Tests |
| FR-13 | `MigrationTests` | Ledger.Api.IntegrationTests |
| FR-13 | `RolePrivilegesTests` | Ledger.Api.IntegrationTests |
| FR-13 | `EntryHttpSurfaceTests` | Ledger.Api.IntegrationTests |
| FR-14 | `CurrentBalanceTests` | Ledger.Api.IntegrationTests |
| FR-14 | `ReadsE2ETests` | Ledger.EndToEnd.Tests |
| FR-15 | `AsOfBalanceTests` | Ledger.Api.IntegrationTests |
| FR-15 | `StableAsOfTests` | Ledger.Api.IntegrationTests |
| FR-15 | `SettledWindowTests` | Ledger.Api.IntegrationTests |
| FR-15 | `BalanceTimeZoneTests` | Ledger.Api.IntegrationTests |
| FR-16 | `AsOfValidationTests` | Ledger.Api.IntegrationTests |
| FR-16 | `AsOfFutureTests` | Ledger.Api.IntegrationTests |
| FR-16 | `QueryInstantReadingTests` | Ledger.Api.IntegrationTests |
| FR-16 | `InstantReadingTests` | Ledger.Api.IntegrationTests |
| FR-17 | `StatementPaginationTests` | Ledger.Api.IntegrationTests |
| FR-17 | `StatementItemsTests` | Ledger.Api.IntegrationTests |
| FR-17 | `StatementDuringWritesTests` | Ledger.Api.IntegrationTests |
| FR-17 | `StatementE2ETests` | Ledger.EndToEnd.Tests |
| FR-18 | `StatementFilterTests` | Ledger.Api.IntegrationTests |
| FR-18 | `StatementQueryReaderTests` | Ledger.Api.IntegrationTests |
| FR-18 | `StatementTimeZoneTests` | Ledger.Api.IntegrationTests |
| FR-18 | `StatementMatchesBalanceTests` | Ledger.Api.IntegrationTests |
| FR-19 | `OutboxPublishingTests` | Ledger.Api.IntegrationTests |
| FR-19 | `EntryOutboxTests` | Ledger.Api.IntegrationTests |
| FR-19 | `EventDeduplicationTests` | Ledger.Api.IntegrationTests |
| FR-19 | `RetentionQueueTests` | Ledger.Api.IntegrationTests |
| FR-19 | `UnroutableMessagesTests` | Ledger.Api.IntegrationTests |
| FR-19 | `EntryEventsE2ETests` | Ledger.EndToEnd.Tests |
| FR-19 | `EventLatencyE2ETests` | Ledger.EndToEnd.Tests |
| FR-19 | `RetentionQueueE2ETests` | Ledger.EndToEnd.Tests |
| FR-19 | `BrokerOutageE2ETests` | Ledger.EndToEnd.Tests |
| FR-20 | `IntegrityDetectsTamperingTests` | Ledger.Api.IntegrationTests |
| FR-20 | `IntegrityNeverCorrectsTests` | Ledger.Api.IntegrationTests |
| FR-20 | `InvariantVerifierTests` | Ledger.Api.IntegrationTests |
| FR-20 | `IntegrityE2ETests` | Ledger.EndToEnd.Tests |
| FR-21 | `RouteScopeMatrixTests` | Ledger.Api.IntegrationTests |
| FR-21 | `JwtValidationTests` | Ledger.Api.IntegrationTests |
| FR-21 | `AuthenticationE2ETests` | Ledger.EndToEnd.Tests |
| FR-22 | `CorrelationIdTests` | Ledger.Api.IntegrationTests |
| FR-22 | `CorrelationConcurrencyTests` | Ledger.Api.IntegrationTests |
| FR-23 | `RegisterEntryTests` | Ledger.Api.IntegrationTests |
| FR-23 | `AuthorizationDeniedAuditTests` | Ledger.Api.IntegrationTests |
| FR-23 | `PostgresAuditTrailTests` | Ledger.Api.IntegrationTests |
| FR-23 | `ReadAuditLogTests` | Ledger.Api.IntegrationTests |
| FR-24 | `ApiReadinessWithDatabaseTests` | Ledger.Api.IntegrationTests |
| FR-24 | `ApiHealthEndpointTests` | Ledger.Api.IntegrationTests |
| FR-24 | `RateLimitBehaviorTests` | Ledger.Api.IntegrationTests |
| FR-24 | `RequestTimeoutTests` | Ledger.Api.IntegrationTests |
| FR-24 | `BrokerOutageE2ETests` | Ledger.EndToEnd.Tests |
| FR-24 | `PostgresOutageE2ETests` | Ledger.EndToEnd.Tests |

## Requisitos não funcionais

As metas não funcionais não passam pela matriz. Latência e vazão se verificam pelos limiares do k6 e pelos testes da categoria `Latency`, consistência pelos testes de concorrência e durabilidade pelos testes de falha. Cada meta e o que a verifica estão em [Requisitos não funcionais](requisitos-nao-funcionais.md), e o que não foi medido está em [Limites conhecidos](../09-qualidade/limites-conhecidos.md).

## Leia também

- [Estratégia de testes](../09-qualidade/estrategia-de-testes.md): como as camadas de teste citadas são organizadas.
- [Testes de concorrência e de falha](../09-qualidade/testes-de-concorrencia-e-falha.md): os testes C1 a C7 e F1 a F10 e os invariantes que protegem.
- [Portões de qualidade](../09-qualidade/portoes-de-qualidade.md): as conferências automáticas, inclusive esta.
