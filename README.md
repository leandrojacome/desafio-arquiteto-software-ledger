# Ledger bancário

Ledger de um banco digital: registra créditos e débitos nas contas dos clientes e responde quanto havia em cada conta em qualquer instante, hoje ou três anos atrás, sem perder nem duplicar dinheiro, mesmo sob estresse. É um sistema interno, chamado por outros sistemas do banco (aplicativo, Pix, cartões, conciliação) e sem interface própria. Está escrito em C# 14 sobre .NET 10, com PostgreSQL 16 e RabbitMQ 3.

É um monolito modular com dois executáveis. A API (`Ledger.Api`) recebe as chamadas, valida o token e grava. O Worker (`Ledger.Worker`) aplica as migrações, publica a caixa de saída no RabbitMQ e faz as podas e a conferência de integridade. A arquitetura está explicada em [docs/](docs/README.md), que tem o mapa das páginas, e o ponto de partida técnico é a [visão geral](docs/01-visao-geral/visao-geral.md).

## Requisitos para rodar

Docker com Compose v2, OpenSSL e curl. O SDK do .NET 10 só é preciso para os testes e para rodar a API e o Worker fora do contêiner. Os comandos abaixo são de shell POSIX, então no Windows valem no Git Bash, que já traz o OpenSSL. PostgreSQL, RabbitMQ e k6 rodam em contêiner e não se instalam.

## Como rodar

Na raiz do projeto, estes comandos criam o `.env`, geram um par de chaves RSA de desenvolvimento e sobem API, Worker, PostgreSQL e RabbitMQ:

```sh
cp .env.example .env
mkdir dev-keys
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out dev-keys/private.pem
openssl pkey -in dev-keys/private.pem -pubout -out dev-keys/public.pem
sed -i "s|^DEV_JWT_PUBLIC_KEY_PEM_B64=.*|DEV_JWT_PUBLIC_KEY_PEM_B64=$(openssl base64 -A -in dev-keys/public.pem)|" .env
docker compose up -d --build --wait
curl http://localhost:8080/health/ready
```

O `.env.example` traz só senhas de desenvolvimento, fracas de propósito, e o `.env` e a pasta `dev-keys/` ficam fora do controle de versão. No macOS, o `sed` pede `-i ''`. A primeira subida constrói as imagens e leva alguns minutos, e o `--wait` só devolve o controle quando os serviços de execução contínua estão saudáveis. As portas são publicadas só em `127.0.0.1`: API em 8080, Worker em 8081, PostgreSQL em 5432, RabbitMQ em 5672 e o painel dele em 15672. Para trocar uma, defina a variável de mesmo nome antes de subir, como em `POSTGRES_PORT=55432 docker compose up -d --build --wait`. Os serviços, as imagens, as variáveis e a forma de rodar dois ambientes ao mesmo tempo estão em [docker/README.md](docker/README.md).

Toda rota de `/v1` exige um token JWT RS256 (emissor `https://idp.local.test`, audiência `ledger-api`, escopos `ledger.read` e `ledger.write`). Estes comandos geram um, válido por uma hora, assinado com a chave privada de `dev-keys/`:

```sh
b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }
now=$(date +%s)
header='{"alg":"RS256","typ":"at+jwt","kid":"dev-local"}'
payload='{"iss":"https://idp.local.test","aud":"ledger-api","sub":"dev-client","client_id":"dev-client","scope":"ledger.read ledger.write","iat":'$now',"nbf":'$now',"exp":'$((now+3600))',"jti":"'$(openssl rand -hex 16)'"}'
unsigned="$(printf '%s' "$header" | b64url).$(printf '%s' "$payload" | b64url)"
TOKEN="$unsigned.$(printf '%s' "$unsigned" | openssl dgst -sha256 -sign dev-keys/private.pem -binary | b64url)"
```

Com o `TOKEN` na sessão, criar uma conta, creditar R$ 1.000,00, consultar o saldo e ler o extrato é isto:

```sh
BASE=http://localhost:8080
ACCOUNT=$(curl -s -X POST "$BASE/v1/accounts" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"holderDocument":"123.456.789-09","currency":"BRL","overdraftLimit":"0.00"}' | sed -n 's/.*"accountId":"\([^"]*\)".*/\1/p')
curl -s -X POST "$BASE/v1/accounts/$ACCOUNT/entries" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: credito-inicial-0001" -d '{"type":"CREDIT","amount":"1000.00","currency":"BRL","description":"Saldo inicial"}'
curl -s "$BASE/v1/accounts/$ACCOUNT/balance" -H "Authorization: Bearer $TOKEN"
curl -s "$BASE/v1/accounts/$ACCOUNT/entries?limit=10" -H "Authorization: Bearer $TOKEN"
```

Repetir o crédito com a mesma chave e o mesmo corpo devolve o lançamento original sem gravar nada, e com a mesma chave e outro corpo a resposta é 422 `IDEMPOTENCY_KEY_REUSED`. As mensagens que a API devolve ao chamador (`title`, `detail` e a `message` de cada campo) estão em português do Brasil, e códigos de erro, nomes de campo e de rota, logs e métricas ficam em inglês. Os instantes entram com fuso horário (`Z` ou um deslocamento como `-03:00`) e saem sempre em UTC, e um instante sem fuso é recusado. O contrato de cada rota está em [Contrato da API REST](docs/05-contratos/api-rest.md), o texto de cada erro em [Catálogo de erros](docs/05-contratos/catalogo-de-erros.md), e os exemplos de estorno, de saldo em um instante e de extrato por período, com a solução dos problemas comuns, em [Execução local](docs/10-implantacao-e-entrega/execucao-local.md).

Para derrubar o ambiente e apagar os dados do banco e do broker:

```sh
docker compose down -v
```

Sem o `-v`, os dados ficam nos volumes para a próxima subida. A API e o Worker também rodam direto na máquina, com `dotnet run --project src/Ledger.Api` e `dotnet run --project src/Ledger.Worker`. Para isso é preciso um PostgreSQL e um RabbitMQ acessíveis, o esquema aplicado (`dotnet run --project src/Ledger.Worker -- --migrate`) e a configuração no ambiente do processo, com os nomes de variável que o `docker-compose.yml` entrega a cada serviço. A lista completa está em [Configuração e linha de comando](docs/05-contratos/configuracao.md).

## Como rodar os testes

Cada camada de teste tem o seu comando:

| O que roda | Comando | Precisa de |
|---|---|---|
| Unidade e arquitetura | `dotnet test --filter 'FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~EndToEnd'` | SDK do .NET 10, sem Docker |
| Integração | `dotnet test tests/Ledger.Api.IntegrationTests` | Docker ligado. PostgreSQL e RabbitMQ sobem por Testcontainers |
| Integração, perfil rápido | `dotnet test tests/Ledger.Api.IntegrationTests --filter "Speed!=Slow"` | Docker ligado. Deixa de fora as classes que esperam queda e religação reais do banco e do broker |
| Fim a fim | `LEDGER_E2E_PROVISION=true dotnet test tests/Ledger.EndToEnd.Tests` | Docker ligado. O próprio teste sobe e derruba a pilha |
| Cobertura do domínio | `dotnet test tests/Ledger.Domain.Tests -p:CollectCoverage=true -p:Include="[Ledger.Domain]*" -p:Threshold=95%2C90 -p:ThresholdType=line%2Cbranch` | SDK do .NET 10. O piso é 95% de linhas e 90% de ramos |
| Cobertura da aplicação | Igual, com `tests/Ledger.Application.Tests`, `-p:Include="[Ledger.Application]*"` e `-p:Threshold=90%2C80` | SDK do .NET 10. O piso é 90% de linhas e 80% de ramos |
| Documento OpenAPI | `LEDGER_OPENAPI_UPDATE=true dotnet test tests/Ledger.Api.IntegrationTests --filter "FullyQualifiedName~OpenApiContractTests"` | SDK do .NET 10, sem Docker. Regenera `docs/05-contratos/openapi.v1.json` depois de uma mudança de contrato proposital |
| Carga com k6 | `LEDGER_TOKEN=$TOKEN K6_SCENARIO=smoke docker compose -f docker-compose.yml -f docker-compose.test.yml --profile load up --build --exit-code-from k6 k6` | Docker ligado e o `TOKEN` da seção anterior |

O filtro da primeira linha leva aspas simples porque o `!` dentro de aspas duplas dispara a expansão de histórico no bash interativo. Os cenários do k6 são `smoke`, `write-spread`, `write-hot-account`, `balance-read`, `statement-read` e `mixed`, e a escala padrão é um décimo do volume das metas (`LOAD_SCALE`). O ambiente de teste publica as mesmas portas do de desenvolvimento, então derrube um antes de subir o outro, e para limpar o de teste use `docker compose -f docker-compose.yml -f docker-compose.test.yml down -v`. Os cenários e os limiares estão descritos em [docker/README.md](docker/README.md).

Teste que depende de ambiente falha quando o ambiente falta, dizendo o que fazer ([documento de arquitetura 0022](docs/03-principios-e-decisoes/documento-arquitetura/0022-testes-dependentes-de-ambiente-falham-por-padrao.md)). `LEDGER_TESTS_DISABLE_DOCKER=true` e `LEDGER_SKIP_E2E=true` ignoram de propósito os testes que dependem de Docker e o fim a fim, e `LEDGER_REQUIRE_DOCKER=true`, que o CI define, desfaz esse opt-out e faz o teste falhar em vez de ser ignorado. A estratégia completa está em [Estratégia de testes](docs/09-qualidade/estrategia-de-testes.md).

A compilação, a formatação e a auditoria de pacotes completam as verificações:

```sh
dotnet build -c Release
dotnet format --verify-no-changes
dotnet list package --vulnerable --include-transitive
```

Aviso de compilação é erro (`Directory.Build.props`, com analisadores no nível máximo e `Nullable` ligado), e a lista de símbolos proibidos e as outras conferências estão em [Garantias automáticas](docs/09-qualidade/portoes-de-qualidade.md). O workflow do GitHub Actions (`.github/workflows/ci.yml`) roda a validação, os testes de unidade e de arquitetura com os pisos de cobertura e os de integração a cada push no `main` e a cada pull request ([GitHub Actions](docs/10-implantacao-e-entrega/github-actions.md)). O pipeline do Azure DevOps (`azure-pipelines.yml`, com os modelos em `pipelines/`) repete essas verificações e acrescenta a conferência do contrato OpenAPI, o teste fim a fim, a varredura de segredos e o empacotamento das imagens, com a publicação opcional num registro ([Pipeline no Azure DevOps](docs/10-implantacao-e-entrega/pipeline-azure-devops.md)).

## Estrutura do projeto

```text
src/
  Ledger.Domain           regras puras: Money, Result, identificadores, lançamento e saldo
  Ledger.Application      casos de uso (handlers), portas e DTOs
  Ledger.Infrastructure   PostgreSQL com Npgsql e Dapper, migrações com DbUp, RabbitMQ, criptografia do documento, Serilog e OpenTelemetry
  Ledger.Api              Minimal API: saúde, autenticação JWT, limites de taxa, erros em Problem Details
  Ledger.Worker           comando --migrate, porta de saúde, publicador do outbox, podas e conferência de integridade
tests/
  Ledger.Domain.Tests, Ledger.Application.Tests e Ledger.Infrastructure.Tests   unidade
  Ledger.Architecture.Tests                                                      camadas, higiene e documentação
  Ledger.Api.IntegrationTests                                                    PostgreSQL e RabbitMQ reais via Testcontainers
  Ledger.EndToEnd.Tests                                                          HTTP contra o ambiente do Compose
  load/ledger.k6.js                                                              cenários de carga
docs/                     documentação de arquitetura, com o ponto de entrada em docs/README.md
docker/                   Dockerfiles, criação dos papéis do PostgreSQL e sondas de saúde
docker-compose.yml        PostgreSQL, RabbitMQ, migração, API e Worker
docker-compose.test.yml   camada do ambiente de teste e de carga
azure-pipelines.yml       pipeline do Azure DevOps, com os modelos em pipelines/
.github/workflows/        workflow de CI do GitHub Actions (ci.yml)
dev-keys/                 par de chaves RSA de desenvolvimento, gerado na subida e ignorado pelo git
```

Na raiz ficam também o `Ledger.sln`, o `global.json`, o `Directory.Build.props` (regras de compilação), o `Directory.Packages.props` (versões de pacote centralizadas), o `BannedSymbols.txt`, o `.editorconfig` e o `.env.example`. A dependência entre as camadas só aponta para dentro, e o `Ledger.Architecture.Tests` derruba a suíte se alguém a inverter.

## Decisões principais

O ledger é imutável: só `INSERT` e `SELECT`, e o erro se corrige por estorno ([documento de arquitetura 0002](docs/03-principios-e-decisoes/documento-arquitetura/0002-ledger-imutavel-somente-insercao.md)). Cada lançamento guarda o saldo depois dele (`balance_after`), o que faz do saldo em qualquer instante uma busca por índice ([documento de arquitetura 0003](docs/03-principios-e-decisoes/documento-arquitetura/0003-saldo-apos-em-cada-lancamento.md)), e o banco decide o saldo corrente com um `UPDATE` condicional, sem leitura seguida de escrita ([documento de arquitetura 0004](docs/03-principios-e-decisoes/documento-arquitetura/0004-saldo-corrente-com-update-condicional.md)). Lançamento, saldo, chave de idempotência e evento entram no mesmo commit, e o Worker publica a caixa de saída no RabbitMQ com entrega ao menos uma vez ([documento de arquitetura 0005](docs/03-principios-e-decisoes/documento-arquitetura/0005-transacao-unica-com-outbox.md) e [0008](docs/03-principios-e-decisoes/documento-arquitetura/0008-rabbitmq-entrega-ao-menos-uma-vez.md)). A repetição de uma chamada é segura porque a `Idempotency-Key` é conferida junto com um hash do corpo ([documento de arquitetura 0006](docs/03-principios-e-decisoes/documento-arquitetura/0006-idempotencia-por-chave-e-hash.md)).

O acesso a dados é PostgreSQL com Npgsql, Dapper e migrações em SQL puro ([documento de arquitetura 0007](docs/03-principios-e-decisoes/documento-arquitetura/0007-postgresql-npgsql-dapper-dbup.md)), sem cache de saldo ([documento de arquitetura 0009](docs/03-principios-e-decisoes/documento-arquitetura/0009-sem-cache-na-v1.md)). A autenticação é por JWT, e o documento do titular é guardado cifrado ([documento de arquitetura 0010](docs/03-principios-e-decisoes/documento-arquitetura/0010-seguranca-jwt-e-criptografia-de-pii.md)). As regras de negócio devolvem `Result` em vez de lançar exceção. O código não usa comentários e a compilação não admite avisos ([documento de arquitetura 0015](docs/03-principios-e-decisoes/documento-arquitetura/0015-sem-comentarios-e-zero-warnings.md)). Cada decisão, com as alternativas descartadas e o que ficou pior, está no [registro de documentos de arquitetura](docs/03-principios-e-decisoes/documento-arquitetura/README.md). O que foi pesado e recusado em conjunto está em [Riscos e alternativas rejeitadas](docs/03-principios-e-decisoes/riscos-e-trade-offs.md), e o que fica para depois, com o gatilho de cada item, em [Evolução futura](docs/11-evolucao/evolucao-futura.md).

## Limites conhecidos

As metas de vazão e de latência (2.000 lançamentos e 10.000 consultas de saldo por segundo, p99 de 150 ms na escrita e de 50 ms no saldo) são dimensionamento, e nenhuma foi medida. O k6 roda em escala reduzida e na mesma máquina da pilha, o que mostra que as rotas respondem e que os invariantes se mantêm sob carga, não que o sistema aguenta a escala. Failover, RPO zero com standby síncrono e o RTO de 15 minutos estão descritos e nunca foram exercitados, porque dependem de uma topologia que o Compose não sobe: ele roda um PostgreSQL só, sem TLS. O provedor de chaves do documento do titular lê uma pasta de segredos, e o adaptador para um cofre externo não foi escrito. Os dois pipelines estão escritos e a estrutura deles é conferida por teste, mas nenhum rodou ainda no serviço.

Cada limite, com o que foi verificado, como seria exercitado e o que mudaria no desenho, está em [Limites conhecidos](docs/09-qualidade/limites-conhecidos.md).
