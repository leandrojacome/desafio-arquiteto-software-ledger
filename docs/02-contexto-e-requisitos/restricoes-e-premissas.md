# Restrições e premissas

Esta página reúne o que limita o desenho (tecnologia, entrega, regulação) e o que foi suposto sobre o negócio e o volume. As restrições valem no código e na configuração. As premissas de dimensionamento são suposições sem medição: a conta de capacidade que as usa está em [Capacidade e escala](../08-resiliencia-e-operacao/capacidade-e-escala.md) e se refaz quando alguém trouxer o dado real. As regras de negócio estão em [Regras de negócio](regras-de-negocio.md) e as metas em [Requisitos não funcionais](requisitos-nao-funcionais.md).

## Restrições de tecnologia e de desenho

| Restrição | Origem | Consequência no desenho |
|---|---|---|
| .NET 10, C# 14 e ASP.NET Core Minimal API | Versão de suporte de longo prazo vigente ([documento de arquitetura 0021](../03-principios-e-decisoes/documento-arquitetura/0021-plataforma-dotnet-10.md)) | O suporte termina em novembro de 2028, e a migração seguinte entra na agenda antes disso |
| PostgreSQL 16 e RabbitMQ 3.13 | [documento de arquitetura 0007](../03-principios-e-decisoes/documento-arquitetura/0007-postgresql-npgsql-dapper-dbup.md), [documento de arquitetura 0008](../03-principios-e-decisoes/documento-arquitetura/0008-rabbitmq-entrega-ao-menos-uma-vez.md) | Nada depende de recurso que só exista em versão mais nova, como a sincronização de slots lógicos entre primário e standby |
| Npgsql, Dapper e migrações em SQL puro com DbUp, sem EF Core | [documento de arquitetura 0007](../03-principios-e-decisoes/documento-arquitetura/0007-postgresql-npgsql-dapper-dbup.md) | O SQL de concorrência é escrito e revisado à mão, e só os tipos de persistência o conhecem |
| Uma moeda por conta, BRL na primeira versão | Premissa de negócio | O campo de moeda existe no banco e no código, mas não há conversão |
| Sem cache, sem réplica de leitura e sem particionamento físico | [documento de arquitetura 0009](../03-principios-e-decisoes/documento-arquitetura/0009-sem-cache-na-v1.md), [Evolução futura](../11-evolucao/evolucao-futura.md) | O desenho funciona sem esses recursos e pode aceitá-los depois sem mudar contrato |
| Código C# sem comentários e compilação sem avisos | [documento de arquitetura 0015](../03-principios-e-decisoes/documento-arquitetura/0015-sem-comentarios-e-zero-warnings.md) | A clareza vem de nomes, métodos pequenos e testes, e os avisos de compilação são tratados como erro |
| Documentação e mensagens ao chamador em português, código, logs e contratos em inglês | [documento de arquitetura 0037](../03-principios-e-decisoes/documento-arquitetura/0037-idioma-das-mensagens-ao-chamador.md) | Nomes físicos e códigos de erro em inglês, e o `title`, o `detail` e a mensagem de cada campo em português |

## Restrições de entrega

A integração contínua roda no Azure DevOps, definida em `azure-pipelines.yml` e nos modelos de `pipelines/`, e também no GitHub Actions, em `.github/workflows/ci.yml`. O [documento de arquitetura 0016](../03-principios-e-decisoes/documento-arquitetura/0016-azure-devops-como-plataforma-de-ci-cd.md) cobre as duas. O ambiente local é Docker Compose com a API, o Worker, o PostgreSQL e o RabbitMQ.

RPO zero e RTO de 15 minutos não são atingíveis com um PostgreSQL único. Por isso a documentação descreve duas topologias: a que o repositório sobe de verdade (um PostgreSQL, um RabbitMQ, a API e o Worker) e a de produção, com um primário e standbys de replicação síncrona, descrita em [Implantação: topologia de produção](../04-modelos-c4/implantacao-producao.md). A de produção deveria atender ao RPO zero e ao RTO de 15 minutos, mas nenhum exercício de falha a confirmou ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)).

## Restrições regulatórias e de privacidade

O ledger trata um único dado pessoal, o documento do titular, e guarda registros financeiros que precisam durar dez anos. O que as exigências externas costumam pedir e o que o ledger oferece em cada caso está em [Regras de negócio](regras-de-negocio.md). Confirmar a conformidade é com o compliance, o jurídico e o encarregado de dados do banco.

## Premissas de negócio

- O sistema é interno: quem chama são outros sistemas do banco, e não o cliente final.
- A conta nasce em outro sistema, que também decide se ela está ativa, bloqueada ou encerrada. O ledger guarda um cadastro mínimo, suficiente para operar e para os testes locais.
- O banco é brasileiro e o horário de negócio é o de Brasília (UTC-3). Os chamadores escrevem os instantes com o deslocamento (`-03:00` ou `Z`), e o ledger guarda, calcula e devolve tudo em UTC ([documento de arquitetura 0036](../03-principios-e-decisoes/documento-arquitetura/0036-politica-de-fusos-horarios.md)).
- O saldo em um instante é o saldo registrado até aquele instante pelo relógio do banco (`recorded_at`). A data de negócio informada pelo chamador (`occurred_at`) é guardada, mas não define o saldo.
- Os chamadores geram uma `Idempotency-Key` estável por intenção de negócio e a reutilizam em cada nova tentativa, e os consumidores de eventos deduplicam pelo `message_id`, igual ao `eventId` do corpo.
- Os relógios dos servidores de aplicação são sincronizados por NTP, porque a tolerância de `occurredAt` e a validade dos tokens dependem deles. O `recorded_at` vem do relógio do banco e não depende dessa sincronização.

## Premissas de dimensionamento

Os números abaixo orientam o desenho e não vieram do banco.

| Item | Valor | Fonte |
|---|---|---|
| Pico de escrita, no total e na mesma conta | 2.000 e 50 lançamentos por segundo | Meta assumida (NFR-01) |
| Pico de leitura de saldo | 10.000 consultas por segundo | Meta assumida (NFR-02) |
| Retenção dos lançamentos | 10 anos, sem remoção física | Premissa regulatória (BR-18) |
| Média anualizada de escrita | 200 lançamentos por segundo, um décimo do pico | Premissa de dimensionamento |
| Contas ativas | 20 milhões | Premissa de dimensionamento |
| Extrato no pico | 500 consultas por segundo | Premissa de dimensionamento |
| Instâncias em produção | De 2 a 6 da API e 2 do Worker | Topologia de produção |
| Retenção das chaves de idempotência | 35 dias | [documento de arquitetura 0017](../03-principios-e-decisoes/documento-arquitetura/0017-retencao-de-35-dias-das-chaves-de-idempotencia.md) |
| Retenção das mensagens publicadas da caixa de saída | 7 dias | Configuração `Outbox:RetentionDays` |

O volume real de escrita é desconhecido. A média de 200 lançamentos por segundo, as 20 milhões de contas e os 500 extratos por segundo no pico são números plausíveis para um banco digital de médio porte. Falta a média diária por tipo de operação dos últimos 90 dias, que só os chamadores podem fornecer.

Falta também confirmar que o prazo máximo de reprocessamento dos chamadores cabe nos 35 dias de retenção das chaves de idempotência, e os donos dos sistemas de Pix e de cartões ainda não responderam. As duas lacunas estão em [Limites conhecidos](../09-qualidade/limites-conhecidos.md), com o que mudaria no desenho se a hipótese caísse.

## Dependências externas

O ledger depende de sistemas e equipes que ele não controla, e cada dependência é uma premissa de integração. Compliance, jurídico e encarregado de dados confirmam as exigências regulatórias e o prazo de retenção, e o plantão recebe os sinais por OpenTelemetry e responde aos alertas.

| Dependência | O que o ledger espera dela |
|---|---|
| Emissor de tokens do banco | Entrega JWT por client credentials, com os escopos `ledger.read` e `ledger.write` e a claim `client_id`, e publica as chaves públicas de validação |
| Sistemas chamadores | Seguem o contrato de idempotência: uma chave por intenção de negócio, reutilizada em toda nova tentativa |
| Sistema de origem das contas | Decide a situação da conta e deixa de chamar o ledger quando ela estiver bloqueada ou encerrada |
| Conciliação e demais consumidores | Declaram suas filas no broker e deduplicam pelo `message_id` |
| Fonte das chaves de dados pessoais | Entrega as chaves de cifragem como arquivos de segredo em uma pasta montada no processo, com custódia e rotação fora do ledger |
| Infraestrutura de entrada | Termina o TLS entre os chamadores e a API e encaminha o endereço de origem |
| Plataforma de dados e de mensageria | Opera PostgreSQL com alta disponibilidade e backups testados e RabbitMQ com filas duráveis |

## Leia também

- [Regras de negócio](regras-de-negocio.md): as regras e as exigências regulatórias que as premissas sustentam.
- [Capacidade e escala](../08-resiliencia-e-operacao/capacidade-e-escala.md): a conta de capacidade refeita a partir destas premissas.
- [Limites conhecidos](../09-qualidade/limites-conhecidos.md): o que ainda não foi medido nem exercitado.
