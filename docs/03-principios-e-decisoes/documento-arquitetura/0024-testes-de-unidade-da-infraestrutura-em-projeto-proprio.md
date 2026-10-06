# Documento de arquitetura 0024: Testes de unidade da infraestrutura em projeto próprio

## Contexto

Os componentes puros da infraestrutura, os que não fazem E/S (cifra, provedor de chaves, telemetria, registro de serviços, classificação de falhas), precisam de teste de unidade, e a pergunta é onde ele mora. Pô-los no `Ledger.Application.Tests` obriga esse projeto a referenciar o `Ledger.Infrastructure`, um assembly que a camada de aplicação, por regra, não pode enxergar. A regra de arquitetura valeria para o código de produção e não para quem o testa, e o projeto de teste da aplicação conseguiria compilar contra um tipo de infraestrutura sem que nada reclamasse. O `InternalsVisibleTo` da infraestrutura também teria de declarar o projeto da aplicação, e os testes de telemetria, que disputam fontes estáticas (`ActivitySource` e `Meter`), pediriam execução em série, uma restrição sem sentido para os testes de handler, que nada têm a ver com isso.

## Decisão

Os testes de unidade que precisam da infraestrutura moram no `Ledger.Infrastructure.Tests`, nas pastas `Observability`, `Security`, `Resilience` e `Health`. O `Ledger.Application.Tests` não referencia a infraestrutura, e o `InternalsVisibleTo` da infraestrutura declara só o `Ledger.Infrastructure.Tests` e o `Ledger.Api.IntegrationTests`.

Os auxiliares que os dois projetos usam (o capturador de logs, o `FakeKeyProvider`, os vetores de segurança, o `WriteHarness` e os dublês de integridade) ficam num lugar só, no projeto da aplicação, e entram no outro por arquivo ligado no `.csproj`, sem um terceiro projeto de apoio. O projeto da infraestrutura desliga a execução em paralelo, como a integração faz, porque os instrumentos de telemetria são globais ao processo. O teste que só lê arquivos do repositório, o `CryptographyBoundaryTests`, mora no `Ledger.Architecture.Tests`, ao lado dos outros guardas de higiene.

## Alternativas descartadas

- Deixar a aplicação referenciar a infraestrutura nos testes. Não custa nada hoje, mas deixa a regra de dependência sem dente justamente onde o código de infraestrutura é mais testado, e cada tipo novo entra na superfície que o projeto da aplicação enxerga.
- Mover esses testes para o `Ledger.Api.IntegrationTests`. Esse projeto já referencia tudo, mas exige Docker e roda em série, e testes de milissegundos não deveriam herdar essa condição.
- Um quarto projeto só com os auxiliares compartilhados. Resolveria a ligação por arquivo ao preço de mais um projeto na solução para poucos arquivos pequenos.

## Consequências

O projeto da aplicação só compila contra a aplicação e o domínio, e a regra de dependência vale também para os testes. Em compensação, os auxiliares compartilhados aparecem duas vezes no gerenciador de projetos (uma real, uma ligada), e um teste novo que precise dos dois mundos exige decidir onde mora. A regra prática é que o teste vai para onde está o tipo que ele exercita. Se os auxiliares ligados passarem de vinte arquivos, ou se um deles precisar de estado próprio em cada projeto, o projeto de apoio compensa.
