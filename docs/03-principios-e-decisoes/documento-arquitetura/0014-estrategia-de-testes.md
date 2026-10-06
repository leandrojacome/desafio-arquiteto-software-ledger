# Documento de arquitetura 0014: Estratégia de testes

## Contexto

Não há frontend, então não há teste de interface. O risco do ledger está em concorrência (dois débitos ao mesmo tempo na mesma conta), idempotência (repetição depois de um timeout), consistência entre lançamento, saldo e outbox, e regras que vivem em SQL, como o `UPDATE` condicional e as restrições únicas. Esse comportamento só aparece num PostgreSQL de verdade, e um teste que simula o banco passa sem provar nada.

## Decisão

Cinco camadas, cada uma com seu projeto em `tests/`.

A unidade fica em três projetos, com xUnit e Shouldly: `Ledger.Domain.Tests`, sem nenhum dublê, `Ledger.Application.Tests`, com NSubstitute só nas portas, e `Ledger.Infrastructure.Tests`, para cifra, chaves, telemetria, registro de serviços e classificação de falhas, lógica da infraestrutura que não precisa de banco. O tempo entra por `FakeTimeProvider`.

A integração, em `Ledger.Api.IntegrationTests`, usa `WebApplicationFactory` contra PostgreSQL 16 e RabbitMQ 3 reais em Testcontainers, com as migrações aplicadas do zero, e cada teste cria as próprias contas em vez de limpar o banco. Sem Docker o teste falha em vez de ser ignorado ([documento de arquitetura 0022](0022-testes-dependentes-de-ambiente-falham-por-padrao.md)). É onde estão os testes dos invariantes: 100 débitos de 10 em paralelo contra saldo 500 dão 50 aceitos e 50 recusados; a mesma `Idempotency-Key` em paralelo grava uma vez só; um `UPDATE` ou `DELETE` em `ledger_entries` é rejeitado pelo banco; com o RabbitMQ parado a escrita segue e o outbox esvazia quando ele volta. A suíte roda em série, porque a telemetria divide estado do processo, e um marcador `Speed=Slow` separa um perfil rápido, que deixa de fora os testes que esperam a queda e a volta reais do broker ou do banco ([documento de arquitetura 0034](0034-suite-de-integracao-em-serie-e-perfil-rapido.md)).

A arquitetura, em `Ledger.Architecture.Tests`, confere a regra de dependência entre camadas e a documentação contra o código. O fim a fim, em `Ledger.EndToEnd.Tests`, faz HTTP contra o Docker Compose nas jornadas principais: criar conta, creditar, debitar, consultar, estornar e paginar o extrato. A carga usa cenários k6 (`tests/load/ledger.k6.js`) com o perfil de 2.000 escritas e 10.000 leituras por segundo e um cenário de conta quente. É a única camada que mede as metas de latência, e a carga completa não roda a cada pull request, porque um executor compartilhado tem ruído demais para provar números absolutos.

Os nomes seguem `Sujeito_Condicao_Resultado`, um comportamento por teste, sem `Thread.Sleep`.

## Alternativas descartadas

- Banco em memória, SQLite ou repositórios falsos. O que precisa ser provado está no SQL, e outro banco tem outro comportamento de lock e de isolamento.
- Pirâmide invertida, só fim a fim. É lenta e frágil, e a falha aponta o sintoma, não a causa.
- Moq e FluentAssertions. O FluentAssertions passou a exigir licença paga para uso comercial a partir da versão 8, e o Moq teve a polêmica do SponsorLink em 2023. NSubstitute e Shouldly cobrem o necessário sem esse risco.
- Teste de mutação e de contrato entre consumidor e provedor. Úteis, mas não há consumidor real para o contrato e a mutação custa tempo de CI sem necessidade agora ([Evolução futura](../../11-evolucao/evolucao-futura.md)).
- Carga completa em todo pull request. Daria números instáveis e uma suíte lenta.

## Consequências

A integração exige Docker na máquina e no CI e é a parte lenta. Cada promessa da documentação aponta para o teste que a prova ([Rastreabilidade de requisitos](../../02-contexto-e-requisitos/rastreabilidade.md)). Há um piso de cobertura, de 95% de linhas e 90% de ramos no domínio e de 90% e 80% na aplicação, mas ele é só um piso: os invariantes importantes são provados por cenário e não por percentual.

O que não se testa: rede, failover, comportamento da infraestrutura de produção e as metas de latência em escala. A queda de conexão e a falha do broker são simuladas localmente, o que não substitui um exercício de recuperação em ambiente real ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md)). O orçamento da suíte completa de integração é de 10 minutos no CI, e acima disso ela deve ser dividida.
