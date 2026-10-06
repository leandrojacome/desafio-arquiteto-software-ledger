# Documento de arquitetura 0034: Suíte de integração em série e perfil rápido

## Contexto

O [documento de arquitetura 0014](0014-estrategia-de-testes.md) fixa 10 minutos como orçamento da suíte de integração. Boa parte dos testes tem a mesma forma: um host da API ou do Worker apontado para um PostgreSQL que não existe (`127.0.0.1`, porta 1). O `PostgresPoolWarmupService` tenta abrir as conexões mínimas de cada fonte e, com o banco ausente, espera o `ConnectionTimeoutSeconds` de 1 segundo em cada uma. O host da API aquece três fontes em sequência, então cada host gasta alguns segundos descobrindo que o banco não está lá, e nenhum desses testes depende do aquecimento.

Paralelizar seria outra saída, mas o `AssemblyBehavior.cs` desliga o paralelismo do projeto, porque as classes dividem o estado do processo: os testes de telemetria leem os medidores por `MeterListener`, que enxerga todos os hosts do processo, e outros alteram variáveis de ambiente. Duas classes rodando juntas contaminariam as contagens uma da outra.

## Decisão

O banco inalcançável dos testes pede pool mínimo zero em todas as fontes (`TestConfiguration.ForUnreachablePostgres`), e o aquecimento não espera conexões que não vão existir. O teste que quer ver o aquecimento falhar com o banco fora pede `MinPoolSize=1` por conta própria.

A suíte roda em série dentro do processo. Um banco por classe no mesmo contêiner fica como recurso para quando um teste precisar de isolamento de dados, não como caminho de paralelismo.

Cinco classes cujo tempo vem de esperar queda e religação reais do broker ou do banco (`BrokerOutageTests`, `PartialConfirmationTests`, `WorkerDatabaseOutageTests`, `BrokerConnectionTests` e `PublisherKilledBetweenPublishAndMarkTests`) levam `[Trait("Speed", "Slow")]`. O perfil rápido da integração exclui essa marca:

```bash
dotnet test tests/Ledger.Api.IntegrationTests --filter "Speed!=Slow"
```

Para encurtar mais, `CONCURRENCY_ITERATIONS=1` reduz de 3 (o padrão) para 1 as repetições dos testes de concorrência, e `--filter "Category!=Resilience"` tira os cenários de resiliência do fim a fim. Fica de fora a espera real, e entra o resto de cada categoria. O perfil completo é o padrão: nos pipelines, o pull request roda o rápido e as demais execuções rodam o completo. Um teste que gasta a maior parte do tempo esperando o relógio, e não trabalhando, leva a mesma marca `Speed=Slow`.

## Alternativas descartadas

- Paralelizar as classes no mesmo processo com um banco por classe. O estado compartilhado do processo impede, e os testes de telemetria teriam de usar um medidor por host em vez do global, uma mudança no código de produção que não se justifica por tempo de teste.
- Dividir a suíte em vários processos, cada um com os seus contêineres. Funcionaria, mas multiplica a memória usada pelo Docker e complica o filtro por partição, e o pool mínimo zero já elimina o custo dominante.
- Baixar os prazos reais dos cenários de queda (circuit breaker, espera de reconexão) só nos testes. Enfraquece o que o teste demonstra, porque o prazo é parte do comportamento que o teste confere.
- Um banco que recusa a conexão na hora, para os testes de banco fora. Mudaria o tipo de falha que o código vê (conexão derrubada em vez de recusada), sem ganho que o pool mínimo zero já não entregue.

## Consequências

O perfil rápido não substitui o completo: não exercita a queda e a volta do broker nem do banco com o Worker em operação, e quem mexe no publicador do outbox, na reconexão ou nos limites de tempo roda o completo. O orçamento de 10 minutos do documento de arquitetura 0014 vale para a suíte completa.

Um host de teste com banco inalcançável por outro caminho que não o `ForUnreachablePostgres` paga de novo a espera do aquecimento, de 1 segundo por fonte, e a duração por teste nos resultados mostra isso de imediato.
