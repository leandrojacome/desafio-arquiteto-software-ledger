# Ambiente local com Docker Compose

Este diretório guarda os Dockerfiles e o que mais o `docker-compose.yml` da raiz precisa para subir o ledger inteiro na máquina de desenvolvimento: PostgreSQL, RabbitMQ, a migração do esquema, a API e o Worker. Não é a topologia de produção. Aqui o PostgreSQL é uma instância só, sem standby, e as portas ficam em `127.0.0.1`. A topologia de produção está em [docs/04-modelos-c4/implantacao-producao.md](../docs/04-modelos-c4/implantacao-producao.md), e o que cada ambiente exige de configuração está em [docs/10-implantacao-e-entrega/ambientes-e-configuracao.md](../docs/10-implantacao-e-entrega/ambientes-e-configuracao.md).

O passo a passo para criar o `.env`, gerar as chaves de desenvolvimento, subir o ambiente e gerar um token está em [Como rodar](../README.md#como-rodar), no README da raiz. Depois disso, o dia a dia são três comandos do Compose:

```sh
docker compose up -d --build --wait
docker compose down
docker compose down -v
```

O primeiro constrói as imagens e espera os serviços de execução contínua ficarem saudáveis. O segundo derruba o ambiente e preserva os dados do banco e do broker, e o terceiro apaga também os volumes.

## O que sobe

| Serviço | Imagem | Porta no host | Para que serve |
|---|---|---|---|
| `postgres` | `ledger-postgres`, derivada de `postgres:16` | `127.0.0.1:5432` | O banco do ledger. O script de inicialização cria o banco `ledger` e os quatro papéis (`ledger_migrator`, `ledger_api`, `ledger_worker`, `ledger_readonly`) |
| `rabbitmq` | `rabbitmq:3.13-management` | `127.0.0.1:5672` (AMQP) e `127.0.0.1:15672` (painel) | Broker dos eventos do outbox. O Worker declara a fila `retention.ledger.entry-registered`, ligada a `ledger.events`, para os eventos esperarem enquanto nenhum consumidor declara a sua |
| `migrator` | `ledger-worker` com o argumento `--migrate` | Nenhuma | Aplica as migrações do DbUp com o papel `ledger_migrator` e termina |
| `dev-keys` | `alpine:3.20` | Nenhuma | Grava a chave pública de desenvolvimento num volume para a API validar tokens |
| `api` | `ledger-api` | `127.0.0.1:8080` | `Ledger.Api`. Saúde em `/health/ready` |
| `worker` | `ledger-worker` | `127.0.0.1:8081` | `Ledger.Worker`. Saúde em `/health/ready` |
| `otel` | `grafana/otel-lgtm`, só com `--profile observability` | `127.0.0.1:3000` | Coletor OTLP e Grafana, para olhar rastros e métricas |

As senhas de desenvolvimento são fracas de propósito e não devem escapar da máquina, por isso o ambiente não aceita conexão de fora. As portas se mudam no `.env` ou na variável de ambiente de mesmo nome, que vale mais que o `.env` (`POSTGRES_PORT`, `RABBITMQ_PORT`, `RABBITMQ_MANAGEMENT_PORT`, `API_PORT`, `WORKER_HEALTH_PORT` e `OTEL_GRAFANA_PORT`). Se uma porta já estiver ocupada, a mais comum sendo a 5432 de outro PostgreSQL, o Docker recusa a subida dizendo qual, e basta trocá-la:

```sh
POSTGRES_PORT=55432 docker compose up -d --build --wait
```

Para ligar o coletor e o Grafana, o `otel` precisa do perfil e a API e o Worker, do endereço do coletor:

```sh
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel:4317 docker compose --profile observability up -d --build --wait
```

A ordem de subida está nos `depends_on` com condição. O `postgres` precisa estar saudável para o `migrator` rodar, a API só sobe depois de o `migrator` e o `dev-keys` terminarem com sucesso, e o Worker espera também o RabbitMQ. A migração é uma etapa de implantação, nunca algo que a API faz ao subir. Se ela falhar, nada mais sobe, e o motivo está em `docker compose logs migrator`.

Os eventos de lançamento esperam na fila `retention.ledger.entry-registered`, que o Worker declara ao conectar, mesmo sem consumidor nenhum. Dá para conferir no painel (`http://localhost:15672`, com o usuário e a senha do RabbitMQ do `.env`) ou pela API de gerenciamento, que mostra a quantidade em `messages_ready`. O painel atualiza as contagens a cada cinco segundos.

```sh
set -a; . ./.env; set +a
curl -s -u "$RABBITMQ_USERNAME:$RABBITMQ_PASSWORD" http://localhost:15672/api/queues/%2F/retention.ledger.entry-registered
```

A fila guarda 24 horas, 1.000.000 de mensagens ou 1 GiB, o que vier primeiro ([documento de arquitetura 0033](../docs/03-principios-e-decisoes/documento-arquitetura/0033-fila-de-retencao-e-publicacao-mandatory.md)).

## Variáveis

Tudo o que o Compose lê vem do `.env`, que o controle de versão ignora. O `.env.example` traz valores de desenvolvimento óbvios, sem nenhum segredo real. As senhas dos quatro papéis do PostgreSQL e a do RabbitMQ não têm padrão no Compose: se faltarem, ele para com uma mensagem pedindo para copiar o `.env.example`. É proposital, porque uma senha padrão escondida no arquivo do Compose é o tipo de coisa que sobrevive até a produção.

O PostgreSQL só cria os papéis na primeira vez que o volume é inicializado. Se uma senha mudar no `.env` depois do primeiro `up`, a API passa a falhar na autenticação com o banco, e o remédio é `docker compose down -v` seguido de um `up` novo. A exigência de senha (`scram-sha-256`) também no soquete local e em `127.0.0.1` dentro do contêiner vem do `POSTGRES_INITDB_ARGS` do Compose e, como os papéis, só vale na primeira inicialização do volume.

As chaves de dados pessoais (`PII_ENCRYPTION_KEY` e `PII_BLIND_INDEX_KEY`) e a do cursor (`CURSOR_SIGNING_KEY`) são 32 bytes em base64. O Compose as entrega com os nomes `Security__Pii__KeySets__1__*` e `Security__Cursor__SigningKey`. A rota `POST /v1/accounts`, que é administrativa, aceita qualquer cliente autenticado com o escopo de escrita neste ambiente, porque o `appsettings.Development.json` da API define `Authorization:AccountProvisioningClients` como `*`. Em produção a lista precisa ser explícita e o `*` é recusado.

## Logs e níveis

A API e o Worker sobem em `Information`, que mostra o que um operador precisa ler: subida e parada, o que o ledger decide (conta criada, lançamento recusado, consulta de saldo auditada) e qualquer aviso ou erro. Para ver cada requisição, as verificações de saúde da API e os ciclos do outbox, suba com `LEDGER_LOG_LEVEL=Debug`, no ambiente ou no `.env`. O ambiente de teste fixa `Warning`. O RabbitMQ sobe com `RABBITMQ_LOG_LEVEL=error`, e `info` devolve o relato completo do broker, que passa de 200 linhas só na subida. O que ainda aparece nos logs de um ambiente saudável, e por quê, está em [Ambientes e configuração](../docs/10-implantacao-e-entrega/ambientes-e-configuracao.md).

A API e o Worker desligam a negociação GSS ou Kerberos do driver do PostgreSQL. O ledger não usa Kerberos, e com a negociação ligada o driver procura `libgssapi_krb5`, que as imagens `aspnet` não trazem, e registra `Cannot load library libgssapi_krb5.so.2` a cada conexão. Instalar a biblioteca só para calar a mensagem aumentaria a imagem sem uso.

## Mais de um ambiente na mesma máquina

O nome do projeto do Compose é `ledger`, ou o valor de `COMPOSE_PROJECT_NAME`. O nome separa contêineres, rede e volumes, mas as portas do host continuam únicas na máquina, então uma segunda cópia precisa de nome novo e das cinco portas trocadas:

```sh
COMPOSE_PROJECT_NAME=ledger-b POSTGRES_PORT=55433 RABBITMQ_PORT=55674 RABBITMQ_MANAGEMENT_PORT=55675 API_PORT=18090 WORKER_HEALTH_PORT=18091 docker compose up -d --build --wait
COMPOSE_PROJECT_NAME=ledger-b docker compose down -v
```

O nome precisa de letras minúsculas, dígitos, hífen e sublinhado, e começar por letra ou dígito. O ambiente de teste acrescenta o sufixo `-test` (`ledger-b-test`). As imagens usam a etiqueta de `LEDGER_IMAGE_TAG`, que é a mesma para todos os projetos: para rodar duas versões do código lado a lado, uma etiqueta para cada.

## Códigos de saída

Os executáveis terminam com o código que diz o que aconteceu, e cada falha vira uma linha só no formato JSON dos logs, com o motivo numa frase e sem pilha de chamadas. O detalhe técnico, quando existe, fica no nível `Debug`.

O `migrator` sai com 0 quando o esquema está na versão do código, com 1 se um script falhou (a transação do script é desfeita, e a linha de erro traz o motivo do banco), com 2 se o banco não foi alcançado (rede, nome, senha ou TLS) ou a trava de migração ficou ocupada além de `Migrations:LockTimeoutSeconds`, e com 3 se a configuração é inválida. A API e o Worker saem com 0 depois de receber o sinal de término e com 3 se a configuração é inválida. Nesse caso a linha `Fatal` nomeia cada chave e a regra quebrada, sem repetir valor, e nada fica escutando. A tabela completa, com o `--inspect-account`, está em [Configuração e linha de comando](../docs/05-contratos/configuracao.md#linha-de-comando-e-códigos-de-saída).

## Autenticação em desenvolvimento

A API roda com `Authentication__Mode=LocalKey`. A chave pública do par RSA gerado em `dev-keys/` (pasta ignorada pelo controle de versão) vai, em base64, para `DEV_JWT_PUBLIC_KEY_PEM_B64` no `.env`. O serviço `dev-keys` a decodifica para um volume nomeado, e a API lê o arquivo em `Authentication__LocalKey__PublicKeyPath`. O projeto usa volume em vez de montar a pasta do host porque a montagem de arquivo do host quebra quando o daemon do Docker não enxerga o sistema de arquivos do Windows, que é o caso de quem roda o motor dentro do WSL.

O token que a API aceita é um JWT RS256 assinado com a chave privada de `dev-keys/`, com o emissor (`iss`) `https://idp.local.test` e a audiência (`aud`) `ledger-api`, que o Compose fixa na API. O comando que o gera, e a chamada de exemplo, estão em [Como rodar](../README.md#como-rodar).

## Contêineres

As imagens da API e do Worker são de vários estágios. O estágio de compilação usa `mcr.microsoft.com/dotnet/sdk:10.0`, restaura só com os `.csproj` copiados antes (para o cache de camadas sobreviver a mudanças de código) e compila com `-warnaserror`. O estágio final usa `mcr.microsoft.com/dotnet/aspnet:10.0` e roda com o usuário sem privilégio da imagem. O Worker usa a imagem `aspnet` porque abre um servidor web mínimo para os health checks. As imagens base dos Dockerfiles, a do RabbitMQ e a do `dev-keys` são fixadas por resumo criptográfico (`@sha256`).

No Compose, `api`, `worker`, `migrator` e `dev-keys` rodam com sistema de arquivos raiz somente leitura, `/tmp` em memória, sem nenhuma capability do Linux e com `no-new-privileges`. O .NET só precisa de `/tmp`.

A sonda de saúde das imagens é o `healthcheck.sh`, que abre um soquete em `/dev/tcp` e confere o `200` do `/health/ready`. As imagens `aspnet` não trazem `curl` nem `wget`, e instalar um deles só para isso aumentaria a superfície do contêiner. O `migrator` desliga a sonda, porque ele termina. A do PostgreSQL é o `docker/postgres/healthcheck.sh`, que só abre a conexão quando o `postmaster.pid` diz `ready`, e por isso não esbarra no banco ao subir nem ao parar.

## Ambiente de testes e carga

O `docker-compose.test.yml` é uma camada sobre o arquivo principal, e não um ambiente separado de verdade:

```sh
docker compose -f docker-compose.yml -f docker-compose.test.yml up -d --build --wait
docker compose -f docker-compose.yml -f docker-compose.test.yml down -v
```

Ele troca o nome do projeto para `<COMPOSE_PROJECT_NAME>-test` (`ledger-test` por padrão), com volumes e rede próprios, então não toca no ambiente de desenvolvimento, embora as portas do host sejam as mesmas e os dois só rodem juntos com as portas de um deles trocadas. Também desliga o `restart`, para que um contêiner derrubado de propósito continue derrubado, baixa o log para `Warning`, desliga a amostragem de rastros, sobe os limites de taxa (a cota por conta para 500 de capacidade e 250 por segundo, e as por chamador bem acima das do arquivo principal, porque um único `client_id` de teste empurra o volume que em produção viria de vários) e põe a conferência de integridade do Worker para rodar a cada minuto e o limiar de atraso do outbox em 5 segundos.

O serviço `k6` vive só nesse arquivo, no perfil `load`. Ele roda na mesma rede da API, então não precisa de `host.docker.internal`, e sobe a API e o Worker como dependências. O token vem de `LEDGER_TOKEN` (o do [README da raiz](../README.md#como-rodar) vale uma hora e o k6 não o renova), e o cenário, de `K6_SCENARIO`:

```sh
LEDGER_TOKEN=$TOKEN K6_SCENARIO=smoke docker compose -f docker-compose.yml -f docker-compose.test.yml --profile load up --build --exit-code-from k6 k6
LEDGER_TOKEN=$TOKEN K6_SCENARIO=mixed LOAD_SCALE=0.1 docker compose -f docker-compose.yml -f docker-compose.test.yml --profile load up --build --exit-code-from k6 k6
```

O comando termina com o código de saída do k6, que reprova quando um limiar falha, e deixa os contêineres parados, então a limpeza é o `down -v` do início desta seção. Os cenários e os limiares estão em `tests/load/ledger.k6.js`, e as variáveis que o Compose repassa ao k6 são `LOAD_SCALE` (padrão 0,1), `K6_RAMP_SECONDS` (120), `K6_HOLD_SECONDS` (300), `K6_COOLDOWN_SECONDS` (30), `K6_HOT_ACCOUNT_RATE` (100), `K6_ACCOUNT_COUNT`, `K6_STATEMENT_ACCOUNTS` (10), `K6_STATEMENT_ENTRIES` (250), `K6_VERIFIED_SAMPLE` (20) e `K6_LATENCY_FACTOR` (1), que multiplica os limiares de latência.

| Cenário | O que faz | Limiares |
|---|---|---|
| `smoke` | 2 jornadas por segundo por 30 s: cria conta, credita, debita, repete a chave, reusa a chave com outro corpo, tenta estourar o saldo, consulta saldo atual e histórico, pagina o extrato, estorna duas vezes | p99 de escrita abaixo de 150 ms, p99 de saldo abaixo de 50 ms, nenhum check falho |
| `write-spread` | Rampa até 2.000 lançamentos por segundo (vezes `LOAD_SCALE`) espalhados em 10.000 contas (vezes `LOAD_SCALE`), com 1% de repetições da mesma chave | p99 abaixo de 150 ms, menos de 0,05% de respostas inesperadas |
| `write-hot-account` | 100 lançamentos por segundo numa conta só (o dobro do teto premissado; `K6_HOT_ACCOUNT_RATE` muda, e 0 tira a conta quente do `mixed`) por 5 minutos | Os mesmos, mais a conferência de encadeamento no fim |
| `balance-read` | Rampa até 10.000 consultas de saldo por segundo (vezes `LOAD_SCALE`), 5% delas com `asOf` | p99 abaixo de 50 ms |
| `statement-read` | Rampa até 500 extratos por segundo (vezes `LOAD_SCALE`), cada um com duas páginas de 100 itens seguidas pelo cursor, em 10 contas semeadas com 250 lançamentos | p99 abaixo de 200 ms |
| `mixed` | Os quatro de volume ao mesmo tempo | Todos |

No `teardown`, o script confere por HTTP, sem ajuda do banco, que a conta quente e uma amostra das demais têm versões sem lacuna, `balanceAfter` encadeado lançamento a lançamento e saldo atual igual à soma do extrato. Qualquer divergência incrementa `ledger_invariant_violations`, e o limiar `count==0` reprova a execução. O `teardown` também confere a conservação em todas as contas de escrita (a soma dos saldos é o financiamento inicial mais os centavos de créditos aceitos menos os de débitos aceitos, lidos dos contadores `ledger_credited_cents` e `ledger_debited_cents`). Fora do `smoke`, um cenário auxiliar pergunta uma vez por segundo ao `/health/ready` do Worker, que no ambiente de teste fica `Degraded` quando o evento pendente mais antigo passa de 5 segundos.

Com o k6, a API, o Worker, o PostgreSQL e o RabbitMQ na mesma máquina, a escala cheia (`LOAD_SCALE=1`) não mede capacidade. O padrão de 0,1 serve para pegar regressão grosseira e exercitar a conferência de invariantes, e com escala 1 o k6 ainda guarda a lista de contas em cada VU e a memória cresce com isso. Os números de capacidade só valem num ambiente dedicado.

## Limitações do desenho local

O PostgreSQL local não tem réplica, então `synchronous_commit = on` custa quase nada aqui, e a latência de escrita é mais otimista que a de produção. O RabbitMQ usa um único usuário administrador (`ledger_worker`), porque o teste fim a fim precisa criar filas. E não há TLS em lugar nenhum, pelo mesmo motivo de as portas ficarem em `127.0.0.1`.
