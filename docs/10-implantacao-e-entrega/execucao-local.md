# Execução local

Da máquina limpa à primeira chamada à API, aos testes e à carga, com a solução dos problemas mais comuns no fim. Todo comando é de shell POSIX e roda na raiz do repositório. O contrato de cada rota está em [Contrato da API REST](../05-contratos/api-rest.md), os erros em [Catálogo de erros](../05-contratos/catalogo-de-erros.md), o que cada ambiente exige de configuração em [Ambientes e configuração](ambientes-e-configuracao.md) e a lógica de cada suíte em [Estratégia de testes](../09-qualidade/estrategia-de-testes.md).

## Requisitos

| Item | Para quê |
|---|---|
| Docker com Compose v2, com o daemon ligado | Subir o ambiente e rodar a integração (Testcontainers), o fim a fim e a carga |
| OpenSSL e `curl` | Gerar as chaves e o token de desenvolvimento e chamar a API. No Windows, os comandos valem no Git Bash, que já traz o OpenSSL |
| SDK do .NET 10 | Compilar, rodar os testes e executar a API e o Worker fora do contêiner. A versão exigida está em `global.json` |
| `jq` (opcional) | Extrair campos das respostas nos exemplos com `curl` |

PostgreSQL, RabbitMQ e k6 rodam em contêiner e não se instalam. A chave pública de desenvolvimento chega à API por um volume nomeado, e não por montagem de arquivo do host, o que também funciona com o daemon do Docker dentro do WSL, que não enxerga as pastas do Windows.

## Subir o ambiente

```sh
cp .env.example .env
mkdir dev-keys
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out dev-keys/private.pem
openssl pkey -in dev-keys/private.pem -pubout -out dev-keys/public.pem
sed -i "s|^DEV_JWT_PUBLIC_KEY_PEM_B64=.*|DEV_JWT_PUBLIC_KEY_PEM_B64=$(openssl base64 -A -in dev-keys/public.pem)|" .env
docker compose up -d --build --wait
curl http://localhost:8080/health/ready
```

O `.env.example` traz só senhas e chaves de desenvolvimento, fracas de propósito, e o `.env` e a pasta `dev-keys/` ficam fora do controle de versão. O par RSA assina os tokens da próxima seção, e a chave pública vai em base64 para `DEV_JWT_PUBLIC_KEY_PEM_B64`, que o Compose exige: com a variável vazia ele recusa subir. No macOS, o `sed` pede `-i ''`.

A primeira subida constrói as imagens e leva alguns minutos, e as seguintes aproveitam o cache de camadas. O `--wait` só devolve o controle quando os serviços de execução contínua estão saudáveis, e a migração (`migrator`) e a gravação da chave (`dev-keys`) rodam uma vez e terminam. A última chamada responde `{"status":"Healthy"}`. Para ligar também o coletor OpenTelemetry e o Grafana (em `http://localhost:3000`), a API e o Worker precisam do endereço do coletor:

```sh
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel:4317 docker compose --profile observability up -d --build --wait
```

### Portas e projetos

Os serviços publicam as portas só em `127.0.0.1`: API em 8080, Worker em 8081, PostgreSQL em 5432, RabbitMQ em 5672 e o painel dele em 15672. Se alguma já estiver ocupada, em geral a 5432 de outro PostgreSQL, o Docker recusa a subida dizendo qual. Para trocar uma porta, a variável de ambiente com o nome dela vale mais que o `.env`: `POSTGRES_PORT`, `RABBITMQ_PORT`, `RABBITMQ_MANAGEMENT_PORT`, `API_PORT`, `WORKER_HEALTH_PORT` e, para o Grafana (3000, só com `--profile observability`), `OTEL_GRAFANA_PORT`. Os padrões estão em [Ambientes e configuração](ambientes-e-configuracao.md).

```sh
POSTGRES_PORT=55432 docker compose up -d --build --wait
```

O nome do projeto do Compose é `ledger`, ou o valor de `COMPOSE_PROJECT_NAME`, e o ambiente de teste acrescenta o sufixo `-test`. O nome separa contêineres, rede e volumes, mas as portas do host continuam únicas: o ambiente de teste publica as mesmas portas do de desenvolvimento, então dois ambientes ao mesmo tempo precisam de portas trocadas, e dois de desenvolvimento precisam também de nome diferente. Para derrubar um ambiente com nome fora do padrão, a mesma variável: `COMPOSE_PROJECT_NAME=ledger-b docker compose down -v`. As regras do nome estão em [Ambientes e configuração](ambientes-e-configuracao.md).

## Token de desenvolvimento

Em desenvolvimento a API valida tokens com a chave pública gravada no `.env`, e o token é um JWT RS256 assinado com a chave privada de `dev-keys/`. Estes comandos geram um válido por uma hora e deixam o resultado em `TOKEN`, que os exemplos seguintes usam na mesma sessão do shell:

```sh
b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }
now=$(date +%s)
header='{"alg":"RS256","typ":"at+jwt","kid":"dev-local"}'
payload='{"iss":"https://idp.local.test","aud":"ledger-api","sub":"dev-client","client_id":"dev-client","scope":"ledger.read ledger.write","iat":'$now',"nbf":'$now',"exp":'$((now+3600))',"jti":"'$(openssl rand -hex 16)'"}'
unsigned="$(printf '%s' "$header" | b64url).$(printf '%s' "$payload" | b64url)"
TOKEN="$unsigned.$(printf '%s' "$unsigned" | openssl dgst -sha256 -sign dev-keys/private.pem -binary | b64url)"
```

O emissor é `https://idp.local.test`, a audiência `ledger-api` e o `client_id` `dev-client`. Para um token só de leitura, tira-se `ledger.write` do `scope`. Para trocar o chamador, o `client_id` e o `sub`. Para um token já vencido, `iat`, `nbf` e `exp` no passado. Toda rota de `/v1` exige token: sem ele a resposta é 401 `UNAUTHENTICATED`, e com um token só de leitura as consultas respondem 200 e uma escrita, 403 `FORBIDDEN`. A criação de conta é rota administrativa, e em desenvolvimento qualquer chamador autenticado com o escopo de escrita a usa (`Authorization:AccountProvisioningClients` igual a `*`). Em produção o modo de chave local é recusado, e a lista de clientes precisa ser explícita ([Autenticação e autorização](../07-consistencia-e-seguranca/autenticacao-e-autorizacao.md)).

## Exemplos de uso

Os exemplos usam `curl`. Os textos livres dos exemplos de `-d` têm só ASCII de propósito, porque o `curl` no Windows pode entregar os argumentos fora de UTF-8, e a API recusa o corpo com `VALIDATION_FAILED` e razão `INVALID_FORMAT`. Com acento, o corpo deve vir de um arquivo, com `--data-binary @corpo.json`, como no exemplo do estorno. Os valores são sempre em reais, e as respostas de erro voltam em português.

Criar uma conta, guardar o identificador, creditar R$ 1.000,00 e debitar R$ 80,00. Cada escrita exige a sua `Idempotency-Key`, os valores são texto decimal, e os dois lançamentos voltam com 201 e `balanceAfter` de `1000.00` e `920.00`:

```bash
BASE=http://localhost:8080

ACCOUNT=$(curl -s -X POST "$BASE/v1/accounts" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"holderDocument":"123.456.789-09","currency":"BRL","overdraftLimit":"0.00"}' | jq -r .accountId)

CREDIT=$(curl -s -X POST "$BASE/v1/accounts/$ACCOUNT/entries" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: credito-inicial-0001" \
  -d '{"type":"CREDIT","amount":"1000.00","currency":"BRL","description":"Saldo inicial"}')

DEBIT=$(curl -s -X POST "$BASE/v1/accounts/$ACCOUNT/entries" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: debito-0001" \
  -d '{"type":"DEBIT","amount":"80.00","currency":"BRL","description":"Pix enviado","reference":"E18236120202610011403s0a1b2c3d4e"}')
```

A criação de conta aceita um `Idempotency-Key` opcional: com ele, repetir o pedido depois de um tempo esgotado devolve a mesma conta, marcada com `Idempotent-Replayed: true`, e sem ele a repetição cria outra. Repetir o débito com a mesma chave e o mesmo corpo devolve o lançamento original, com 201 e `Idempotent-Replayed: true`, sem gravar nada, e com a mesma chave e outro valor a resposta é 422 `IDEMPOTENCY_KEY_REUSED`, sem tocar em saldo:

```bash
curl -i -X POST "$BASE/v1/accounts/$ACCOUNT/entries" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: debito-0001" \
  -d '{"type":"DEBIT","amount":"80.00","currency":"BRL","description":"Pix enviado","reference":"E18236120202610011403s0a1b2c3d4e"}'

curl -s -X POST "$BASE/v1/accounts/$ACCOUNT/entries" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: debito-0001" \
  -d '{"type":"DEBIT","amount":"90.00","currency":"BRL","description":"Pix enviado","reference":"E18236120202610011403s0a1b2c3d4e"}'
```

As respostas do débito e da chave reutilizada têm este formato, com identificadores que mudam a cada execução:

```json
{"entryId":"01a10d48-d5b4-7bf5-9316-4508a10bd5b0","accountId":"01a10d48-d2eb-7ae8-b495-5a005978a10a","accountVersion":2,"type":"DEBIT","amount":"80.00","currency":"BRL","balanceAfter":"920.00","occurredAt":"2026-03-14T18:17:19.541767Z","recordedAt":"2026-03-14T18:17:19.541767Z","reversesEntryId":null,"description":"Pix enviado","reference":"E18236120202610011403s0a1b2c3d4e"}
```

```json
{"type":"https://ledger.bank.internal/problems/idempotency-key-reused","title":"Chave de idempotência reutilizada em outra requisição","status":422,"detail":"A chave de idempotência já foi usada com um corpo de requisição diferente nesta conta.","instance":"/v1/accounts/01a10d48-d2eb-7ae8-b495-5a005978a10a/entries","code":"IDEMPOTENCY_KEY_REUSED","correlationId":"01a10d48d88d7a24bda8c487ea2983b2","traceId":"9eb1ae22a2b73fdd721c8b6ea187b4a2"}
```

O saldo de agora devolve `920.00`, sem o campo `settled`, que só existe no saldo com `asOf`. O saldo no instante do crédito devolve `1000.00`, e `settled` fica `true` quando o instante pedido já tem mais de 5 segundos. O `asOf` é um instante com fuso horário, `Z` ou um deslocamento (o horário de Brasília é `-03:00`), e a resposta o devolve sempre em UTC. O fuso é obrigatório, e um `+` cru na query string vale espaço, então um deslocamento positivo como `+00:00` vai como `%2B`.

```bash
curl -s "$BASE/v1/accounts/$ACCOUNT/balance" -H "Authorization: Bearer $TOKEN"

AS_OF=$(echo "$CREDIT" | jq -r .recordedAt)
curl -s "$BASE/v1/accounts/$ACCOUNT/balance?asOf=$AS_OF" -H "Authorization: Bearer $TOKEN"

curl -s "$BASE/v1/accounts/$ACCOUNT/balance?asOf=2026-03-10T15:03:47-03:00" -H "Authorization: Bearer $TOKEN"
```

A última consulta é um instante de Brasília (a data é um exemplo), e devolve o saldo daquele instante, `0.00` se a conta ainda não tinha lançamento.

O estorno é total e é um lançamento novo, do tipo oposto, com `reversesEntryId` apontando para o original, e a resposta é 201 com `balanceAfter` de volta em `1000.00`. Estornar o mesmo lançamento outra vez, com outra chave, dá 409 `ENTRY_ALREADY_REVERSED`. O passado não muda com o estorno: o saldo no instante do débito continua `920.00`, e o extrato mostra os três lançamentos do mais recente ao mais antigo, com `nextCursor` nulo quando acaba.

```bash
ENTRY=$(echo "$DEBIT" | jq -r .entryId)

cat > estorno.json <<'EOF'
{"description":"Cobrança em duplicidade"}
EOF

curl -s -X POST "$BASE/v1/accounts/$ACCOUNT/entries/$ENTRY/reversals" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: estorno-debito-0001" \
  --data-binary @estorno.json

AS_OF=$(echo "$DEBIT" | jq -r .recordedAt)
curl -s "$BASE/v1/accounts/$ACCOUNT/balance?asOf=$AS_OF" -H "Authorization: Bearer $TOKEN"

curl -s "$BASE/v1/accounts/$ACCOUNT/entries?limit=10" -H "Authorization: Bearer $TOKEN"
```

O extrato aceita período, com `from` e `to` como instantes com fuso, e o `to` é exclusivo. O dia 10 de março em Brasília vai de meia-noite a meia-noite com `-03:00`, e basta trocar as datas pelo dia que se quer consultar:

```bash
curl -s "$BASE/v1/accounts/$ACCOUNT/entries?from=2026-03-10T00:00:00-03:00&to=2026-03-11T00:00:00-03:00" -H "Authorization: Bearer $TOKEN"
```

O corpo de cada resposta está em [Contrato da API REST](../05-contratos/api-rest.md) e em [Catálogo de erros](../05-contratos/catalogo-de-erros.md), e o mesmo roteiro roda no fim a fim (`AccountsE2ETests`, `EntriesE2ETests`, `ReadsE2ETests`).

## Testes

Os filtros levam aspas simples porque o `!` dentro de aspas duplas dispara a expansão de histórico no bash interativo.

| O que roda | Comando | Precisa de |
|---|---|---|
| Unidade e arquitetura | `dotnet test --filter 'FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~EndToEnd'` | SDK do .NET 10, sem Docker |
| Integração | `dotnet test tests/Ledger.Api.IntegrationTests` | Docker ligado. PostgreSQL e RabbitMQ sobem por Testcontainers |
| Integração, perfil rápido | `dotnet test tests/Ledger.Api.IntegrationTests --filter 'Speed!=Slow'` | Docker ligado. Deixa de fora as classes que esperam queda e religação reais do banco e do broker |
| Fim a fim | `LEDGER_E2E_PROVISION=true dotnet test tests/Ledger.EndToEnd.Tests` | Docker ligado. O próprio teste sobe e derruba a pilha |
| Cobertura do domínio | `dotnet test tests/Ledger.Domain.Tests -p:CollectCoverage=true -p:Include="[Ledger.Domain]*" -p:Threshold=95%2C90 -p:ThresholdType=line%2Cbranch` | SDK do .NET 10. O piso é 95% de linhas e 90% de ramos |
| Cobertura da aplicação | `dotnet test tests/Ledger.Application.Tests -p:CollectCoverage=true -p:Include="[Ledger.Application]*" -p:Threshold=90%2C80 -p:ThresholdType=line%2Cbranch` | SDK do .NET 10. O piso é 90% de linhas e 80% de ramos |
| Documento OpenAPI | `LEDGER_OPENAPI_UPDATE=true dotnet test tests/Ledger.Api.IntegrationTests --filter 'FullyQualifiedName~OpenApiContractTests'` | SDK do .NET 10, sem Docker. Regenera `docs/05-contratos/openapi.v1.json` depois de uma mudança de contrato proposital |

Os testes de integração sobem o próprio PostgreSQL e o próprio RabbitMQ e não encostam no ambiente do Compose. No comando de cobertura, o `-p:` evita que o Git Bash trate `/p:` como caminho, a vírgula do piso vai como `%2C` porque o MSBuild a usa para separar propriedades, e o `Include` limita a medição ao assembly do projeto, já que os testes da aplicação também passam pelo domínio. O comando falha abaixo do piso. Para gravar o relatório Cobertura, acrescente `-p:CoverletOutputFormat=cobertura -p:CoverletOutput=../../TestResults/coverage/ledger-domain`, um caminho relativo ao projeto de teste. Para que cada projeto grave também um `.trx`, os pipelines passam `--logger "trx" --results-directory TestResults`.

O fim a fim aceita `--filter 'Category!=Resilience'`, que deixa de fora os cenários que derrubam componentes de verdade. Sem `LEDGER_E2E_PROVISION`, ele se liga a um ambiente já de pé, subido com a camada de teste por cima do Compose principal: `docker compose -f docker-compose.yml -f docker-compose.test.yml up -d --build --wait`. Nesse caso lê as credenciais do `.env`, assina o token com `dev-keys/private.pem` e, se o Compose subiu com `POSTGRES_PORT` diferente de 5432, precisa da mesma variável no `.env` ou exportada na sessão ([Estratégia de testes](../09-qualidade/estrategia-de-testes.md)).

Teste que depende de ambiente falha quando o ambiente falta, com uma mensagem que diz o que fazer ([documento de arquitetura 0022](../03-principios-e-decisoes/documento-arquitetura/0022-testes-dependentes-de-ambiente-falham-por-padrao.md)). `LEDGER_TESTS_DISABLE_DOCKER=true` e `LEDGER_SKIP_E2E=true` ignoram de propósito os testes que dependem de Docker e o fim a fim, e `LEDGER_REQUIRE_DOCKER=true`, que os pipelines definem, desfaz esse opt-out e faz o teste falhar em vez de ser ignorado.

A compilação, a formatação e a busca de pacotes vulneráveis completam as verificações:

```sh
dotnet build -c Release
dotnet format --verify-no-changes
dotnet list package --vulnerable --include-transitive
```

Aviso de compilação é erro. O último comando só relata: os pipelines o rodam com `DOTNET_CLI_UI_LANGUAGE=en`, procuram no relatório a frase `has the following vulnerable packages` e reprovam se ela aparecer.

## Carga

O k6 roda em contêiner, na mesma rede da API, pela camada de teste do Compose. O cenário `smoke` é a carga curta:

```sh
LEDGER_TOKEN=$TOKEN K6_SCENARIO=smoke docker compose -f docker-compose.yml -f docker-compose.test.yml --profile load up --build --exit-code-from k6 k6
```

Os cenários são `smoke`, `write-spread`, `write-hot-account`, `balance-read`, `statement-read` e `mixed`, e a escala padrão é um décimo do volume das metas (`LOAD_SCALE`). O código de saída é o do k6, de modo que um limiar estourado reprova o comando. Os cenários, os limiares e as variáveis de ajuste estão em [Estratégia de testes](../09-qualidade/estrategia-de-testes.md). As portas são as do desenvolvimento, então derrube esse ambiente antes ([Portas e projetos](#portas-e-projetos)).

## Derrubar

```sh
docker compose down
docker compose down -v
```

O primeiro preserva os dados do banco e do broker, e o segundo também os apaga, o que é o caminho quando uma senha do `.env` muda depois do primeiro `up` ([Ambientes e configuração](ambientes-e-configuracao.md)). Para o ambiente de teste e a carga, `docker compose -f docker-compose.yml -f docker-compose.test.yml --profile load down -v`.

## Rodar sem o Docker Compose

Para depurar no editor, a API e o Worker rodam na máquina contra o PostgreSQL e o RabbitMQ do Compose. A migração é uma etapa à parte, e a API nunca migra ao subir. Os `export` dão aos processos as senhas e as chaves que o Compose passa por variável de ambiente, e sem elas a API e o Worker recusam subir, nomeando a chave que falta, com código 3. As portas acompanham o que o Compose publicou, e sem elas os processos tentam as padrões. A chave pública do modo `LocalKey` precisa de caminho absoluto:

```bash
docker compose up -d --wait postgres rabbitmq
docker compose run --rm migrator
set -a; . ./.env; set +a
export ASPNETCORE_ENVIRONMENT=Development
export Postgres__Port="$(docker compose port postgres 5432 | cut -d: -f2)"
export Security__Pii__ActiveKeyVersion=1
export Security__Pii__KeySets__1__EncryptionKey="$PII_ENCRYPTION_KEY"
export Security__Pii__KeySets__1__BlindIndexKey="$PII_BLIND_INDEX_KEY"
```

Em um terminal, a API:

```bash
export ASPNETCORE_URLS=http://localhost:5080
export Postgres__Sources__Write__Password="$LEDGER_API_PASSWORD"
export Postgres__Sources__Balance__Password="$LEDGER_API_PASSWORD"
export Postgres__Sources__Statement__Password="$LEDGER_API_PASSWORD"
export Security__Cursor__SigningKey="$CURSOR_SIGNING_KEY"
export Authentication__LocalKey__PublicKeyPath="$PWD/dev-keys/public.pem"
dotnet run --project src/Ledger.Api
```

Em outro terminal, depois de repetir os `export` do primeiro bloco, o Worker:

```bash
export ASPNETCORE_URLS=http://localhost:5081
export Postgres__Sources__Worker__Password="$LEDGER_WORKER_PASSWORD"
export RabbitMq__Port="$(docker compose port rabbitmq 5672 | cut -d: -f2)"
export RabbitMq__Username="$RABBITMQ_USERNAME"
export RabbitMq__Password="$RABBITMQ_PASSWORD"
dotnet run --project src/Ledger.Worker
```

Depois, `curl http://localhost:5080/health/ready` e `curl http://localhost:5081/health/ready` respondem `{"status":"Healthy"}`, e as rotas da API aceitam o `TOKEN` da seção anterior. O `appsettings.Development.json` da API fixa o modo `LocalKey` e o emissor `https://idp.local.test`, o mesmo do token, e a lista completa de chaves está em [Configuração e linha de comando](../05-contratos/configuracao.md).

## Solução de problemas

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| O Docker não responde | O serviço do Docker não está ligado, ou `DOCKER_HOST` aponta para o lugar errado | Iniciar o Docker e repetir. Com o daemon no WSL, conferir `DOCKER_HOST` |
| O `up` falha dizendo que a porta já está em uso | Outro processo ocupa a porta, em geral a 5432 | Trocar a porta pela variável de mesmo nome e repetir |
| O Compose recusa subir pedindo o `.env` ou `DEV_JWT_PUBLIC_KEY_PEM_B64` | `.env` ausente, ou a variável vazia | Refazer os passos de [Subir o ambiente](#subir-o-ambiente), do `cp` ao `sed` |
| A API falha na autenticação com o banco depois de trocar uma senha | Os papéis do PostgreSQL só são criados na primeira inicialização do volume | `docker compose down -v` e uma subida nova |
| Nenhum serviço sobe depois do `postgres` | A migração falhou | `docker compose logs migrator`. Os códigos de saída estão em [Ambientes e configuração](ambientes-e-configuracao.md) |
| 401 `UNAUTHENTICATED` | Token ausente, vencido, ou assinado por um par de chaves diferente do que o `.env` carrega | Gerar um token novo. Se o par de chaves foi regenerado, atualizar o `.env` e recriar o ambiente com `down -v` |
| 403 `FORBIDDEN` | Token só de leitura numa rota de escrita, ou chamador fora da lista de criação de conta | Gerar o token com `ledger.write` |
| 400 `VALIDATION_FAILED` com a razão `INVALID_FORMAT` em texto com acento | O `curl` no Windows entregou o corpo fora de UTF-8 | Enviar o corpo de um arquivo com `--data-binary @corpo.json` |
| 400 `INVALID_AS_OF` com um `asOf` de fuso positivo, como `+00:00` | O `+` cru na query string vale espaço | Escrever `%2B` no lugar do `+` |
| 400 com a razão `MISSING_TIME_ZONE`, ou 400 `INVALID_AS_OF` pedindo o fuso | O instante foi escrito sem `Z` nem deslocamento | Acrescentar o fuso: `Z`, ou `-03:00` para o horário de Brasília |
| `docker compose logs` mostra `Warning`, `Error` ou `Fatal` | Defeito a investigar, salvo as exceções deliberadas | Ler a linha, que traz o motivo numa frase. O aviso de chave reutilizada com outro corpo é esperado depois do exemplo de chave reutilizada. As demais exceções estão em [Ambientes e configuração](ambientes-e-configuracao.md) |
| O Worker responde 503 em `/health/ready` | Banco ou broker inacessível, ou esquema atrás do código | `docker compose logs worker` e [Saúde e observabilidade](../08-resiliencia-e-operacao/saude-e-observabilidade.md) |
| Teste de integração falha pedindo Docker | O Docker não está ligado | Ligar o Docker, ou definir `LEDGER_TESTS_DISABLE_DOCKER=true` para ignorar de propósito |
| Fim a fim falha dizendo que a API não respondeu | Nenhum ambiente de pé e `LEDGER_E2E_PROVISION` ausente | Rodar com `LEDGER_E2E_PROVISION=true`, subir o ambiente de teste, ou definir `LEDGER_SKIP_E2E=true` |
| A restauração ou a compilação mostra `NU1900` | O NuGet não alcançou `api.nuget.org` para consultar vulnerabilidades | Conferir rede, DNS e proxy e rodar `dotnet restore --force`: o aviso de uma restauração que falhou reaparece nas seguintes até uma restauração forçada, e vira erro com `-warnaserror` sem restauração prévia |
