# Autenticação e autorização

O ledger é um servidor de recursos: não emite token nem guarda segredo de cliente. O servidor de autorização do banco emite, e cada sistema chamador (aplicativo, Pix, cartões, conciliação) obtém o seu token pelo fluxo de client credentials, com credencial própria. A API valida o token localmente, em toda requisição e antes de qualquer código de negócio, e usa o `client_id` dele como identidade do chamador em lançamentos, logs, métricas e auditoria. O modelo de ameaças está em [Segurança](seguranca.md), e a ordem das etapas do pipeline HTTP, em [Fluxo: ciclo de vida de uma requisição](../06-fluxos/ciclo-de-vida-da-requisicao.md).

## O que a API valida em todo token

A validação não consulta o emissor a cada requisição: a API guarda as chaves públicas dele e confere o token contra elas. Os itens abaixo são aplicados pela `ConfigureJwtBearerOptions` e pelo evento `TokenValidated` da `AuthenticationEvents`, e o conjunto de opções é validado na subida pelo `JwtAuthenticationOptionsValidator`.

| Item | Regra | Configuração |
|---|---|---|
| Assinatura | Chaves públicas do emissor, obtidas pelo JWKS e guardadas em cache. O algoritmo é restrito a RS256 e ES256, e token com `alg` igual a `none` é recusado | `Authentication:Authority`, `Authentication:Mode` |
| `iss` | Igual ao emissor configurado | `Authentication:Issuer` |
| `aud` | Contém o valor configurado, `ledger-api` na configuração entregue | `Authentication:Audience` |
| `exp` | Obrigatório, com tolerância de relógio de 30 segundos | Fixo no código |
| `nbf` | Se existir, vale com a mesma tolerância | Fixo no código |
| Vida do token | `exp` menos o menor valor entre `nbf` e `iat` não passa do teto. Token sem `iat` e sem `nbf` é recusado, porque a vida dele não se mede | `Authentication:MaxTokenLifetimeMinutes` |
| `typ` | `at+jwt` ou `application/at+jwt` (RFC 9068). Outro tipo, ou nenhum, é recusado | `Authentication:RequireAccessTokenType`, ligado por padrão |
| `scope` | Escopos separados por espaço, comparados de forma exata e sensível a maiúsculas | Políticas de autorização |
| `client_id` | Obrigatório, uma única claim, de 1 a 128 caracteres ASCII visíveis (`0x21` a `0x7E`), porque a coluna é `varchar(128)` | Fixo no código |

A tolerância de 30 segundos substitui os cinco minutos do padrão do framework, que esticariam em cinco minutos a vida de um token curto, e pressupõe servidores sincronizados por NTP. O teto de vida padrão é de 15 minutos e aceita de 1 a 1.440. Fora de `Development` e `Testing`, a subida é recusada com mais de 60. O teto existe para que a promessa de que um token roubado ou revogado deixa de valer em poucos minutos não dependa só de o emissor estar bem configurado ([documento de arquitetura 0010](../03-principios-e-decisoes/documento-arquitetura/0010-seguranca-jwt-e-criptografia-de-pii.md)). O `typ` impede que outro tipo de JWT do mesmo emissor, com a audiência certa, sirva de token de acesso. Se o emissor real não o preenche, `Authentication:RequireAccessTokenType=false` desliga só essa checagem.

A regra de algoritmo vale para o modo `Authority`, o único que a produção aceita. O modo `LocalKey` é exceção de `Development` e `Testing`: com `Authentication:LocalKey:PublicKeyPath` valida RS256 ou ES256 contra a chave pública local (RSA de pelo menos 2.048 bits ou curva P-256), e sem o caminho valida HS256 contra a chave simétrica de `Authentication:LocalKey:SigningKey`, de pelo menos 32 caracteres.

## Identidade do chamador

O `client_id` é a identidade gravada em `ledger_entries.client_id`, em todo evento de auditoria e nos rótulos de métrica. A claim é lida pelo nome que o emissor enviou, porque `MapInboundClaims` fica desligado. Um token sem `client_id`, ou com o valor fora do formato, é autenticado, mas a autorização o recusa com 403. A claim fora do formato ou repetida é removida do usuário no `TokenValidated`, para o valor nunca chegar ao banco, ao log nem a um rótulo.

Qualquer chamador autenticado com o escopo certo opera qualquer conta, porque o ledger não conhece os clientes finais. O raio de impacto e as medidas que o limitam estão em [Segurança](seguranca.md), e a pergunta sobre restringir cada chamador às operações que usa, em [Questões em aberto](../03-principios-e-decisoes/questoes-em-aberto.md).

## O que cada rota exige

| Rota | Política | Escopo exigido | Classe de limite |
|---|---|---|---|
| `POST /v1/accounts` | `AccountProvisioning` | `ledger.write` e `client_id` na lista de provisionamento | Escrita |
| `POST /v1/accounts/{accountId}/entries` | `ledger.write` | `ledger.write` | Escrita |
| `POST /v1/accounts/{accountId}/entries/{entryId}/reversals` | `ledger.write` | `ledger.write` | Escrita |
| `GET /v1/accounts/{accountId}/balance` | `ledger.read` | `ledger.read` | Saldo |
| `GET /v1/accounts/{accountId}/entries` | `ledger.read` | `ledger.read` | Extrato |
| `GET /health/live`, `GET /health/ready` | Anônima | Nenhum | Fora dos limites |

Os escopos são independentes e valem por operação, não por conta: quem só tem `ledger.write` não chama saldo nem extrato, e a conciliação recebe só `ledger.read`.

A política `AccountProvisioning` soma ao escopo um segundo requisito, o `client_id` constar em `Authorization:AccountProvisioningClients`. Não há um terceiro escopo para criar conta, porque o conjunto de escopos é parte do contrato com o emissor e mudá-lo custa mais que uma lista em configuração. O valor `*` (qualquer chamador com `ledger.write`) só é aceito em `Development` e `Testing`. Uma rota registrada sem política explícita cai na política padrão, que exige usuário autenticado com a claim `client_id` e nenhum escopo. O `EndpointPolicyCoverageTests` percorre as rotas da API e falha se achar uma de negócio sem política ou com classe de limite incoerente com ela.

## O que o chamador recebe

Todo corpo de recusa é `application/problem+json` no formato do [Catálogo de erros](../05-contratos/catalogo-de-erros.md). Nenhuma resposta traz o motivo da recusa, o token, o instante de expiração ou o nome do emissor, e o `WWW-Authenticate` também não leva `error_description`: o padrão do `JwtBearer` escreveria o motivo, e dois ajustes o impedem (`IncludeErrorDetails` desligado e o evento `Challenge`). O motivo detalhado vai para o log e para a métrica, onde serve a quem opera e não a quem ataca.

| Situação | Status | `code` | `WWW-Authenticate` | Motivo no log e na métrica |
|---|---|---|---|---|
| Sem cabeçalho `Authorization`, ou sem o esquema `Bearer` | 401 | `UNAUTHENTICATED` | `Bearer` | `missing_token` |
| Token malformado, assinatura inválida, algoritmo não permitido, emissor ou audiência errados, sem `exp`, `typ` diferente de `at+jwt`, vida acima do teto ou sem `iat` e sem `nbf` | 401 | `UNAUTHENTICATED` | `Bearer error="invalid_token"` | `invalid_token` |
| Token expirado além da tolerância, ou `nbf` no futuro além dela | 401 | `UNAUTHENTICATED` | `Bearer error="invalid_token"` | `expired` |
| Token válido sem a claim `client_id` | 403 | `FORBIDDEN` | `Bearer error="insufficient_scope"` | `missing_client_id` |
| Token válido com `client_id` fora do formato, ou com mais de uma claim | 403 | `FORBIDDEN` | `Bearer error="insufficient_scope"` | `invalid_client_id` |
| Token válido sem o escopo da rota | 403 | `FORBIDDEN` | `Bearer error="insufficient_scope", scope="ledger.write"` (ou `ledger.read`) | `insufficient_scope` |
| Escopo certo, mas cliente fora da lista de provisionamento | 403 | `FORBIDDEN` | `Bearer error="insufficient_scope"` | `insufficient_scope` |

`missing_token` se distingue dos demais porque o evento `Challenge` não traz falha de autenticação quando não havia token. Com o banco inalcançável, 401 e 403 saem iguais, porque nenhum desses passos abre conexão. A única gravação no banco ligada à recusa é a da negação de escrita, que acontece fora do caminho da resposta ([Trilha de auditoria](trilha-de-auditoria.md)).

## Ordem das etapas e origem do chamador anônimo

```text
ForwardedHeaders (só com KnownNetworks) -> correlação -> cabeçalhos de segurança -> log da requisição
  -> tratamento de exceção -> páginas de status -> prazo de 3 s
  -> autenticação -> limites -> autorização -> endpoint
```

A autenticação vem antes dos limites porque a cota por chamador precisa do `client_id`, mas ela só identifica: o 401 e o 403 saem da autorização, que vem depois, para que um pedido barrado por cota não gaste a avaliação de política. Como os limites rodam antes da recusa, a cota por conta só vale para quem a autorização aceitaria (autenticado, com `client_id` válido e `ledger.write`). Os valores estão em [Limites de taxa e de concorrência](../08-resiliencia-e-operacao/limites-de-taxa-e-concorrencia.md).

Para quem não tem token, a cota é particionada pela origem da conexão. `Security:ForwardedHeaders:KnownNetworks` lista, em CIDR, as redes dos proxies confiáveis, e só com a lista os cabeçalhos `X-Forwarded-For` e `X-Forwarded-Proto` valem, com `ForwardLimit` igual a 1, que conta apenas a entrada acrescentada pelo proxy mais próximo. Sem a lista, o padrão, os cabeçalhos são ignorados, o middleware nem é registrado e a origem é a do soquete. Uma entrada que não seja CIDR, ou seja `/0`, impede a subida (`ForwardedHeadersSettingsValidator`). Atrás de um ingress, a rede do ingress precisa constar da lista, ou todo pedido anônimo do cluster divide uma cota só.

## Modos por ambiente

Só `Development` e `Testing` são permissivos. Qualquer outro valor de `ASPNETCORE_ENVIRONMENT`, inclusive `Staging`, um erro de digitação ou a variável vazia, recebe o tratamento de produção. A escolha é invertida de propósito: um atalho de autenticação que vaza para produção é o tipo de defeito que a configuração precisa recusar sozinha.

| Item | Desenvolvimento | Teste | Produção e qualquer outro nome |
|---|---|---|---|
| `Authentication:Mode` | `LocalKey`, com o par de chaves RSA da seção seguinte | `LocalKey`, com chave simétrica em memória | `Authority`. `LocalKey` é recusado na subida |
| `Authentication:RequireHttpsMetadata` | `false` | `false` | `true`, e a autoridade precisa ser `https`. Outra combinação impede a subida |
| `Authentication:MaxTokenLifetimeMinutes` | 240 no `appsettings.Development.json` | Valor do teste | Até 60. O padrão é 15 |
| `Authorization:AccountProvisioningClients` | `["*"]` | `["*"]` | Lista explícita. `*` e lista vazia impedem a subida. Entrada em branco impede a subida em qualquer ambiente |
| `RateLimiting:Enabled` | `true` | `false`, exceto nos testes de limite | `true`. `false` impede a subida |

O `ProductionGuardsTests` cobre a recusa para cada nome de ambiente. Os valores de cada chave estão em [Configuração e linha de comando](../05-contratos/configuracao.md) e em [Ambientes e configuração](../10-implantacao-e-entrega/ambientes-e-configuracao.md).

## Chave e token de desenvolvimento

A chave de desenvolvimento é um par RSA criado na máquina de quem desenvolve, em `dev-keys/`, que o git ignora. A chave pública vai para o `.env` em base64.

```bash
cp .env.example .env
mkdir dev-keys
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out dev-keys/private.pem
openssl pkey -in dev-keys/private.pem -pubout -out dev-keys/public.pem
sed -i "s|^DEV_JWT_PUBLIC_KEY_PEM_B64=.*|DEV_JWT_PUBLIC_KEY_PEM_B64=$(openssl base64 -A -in dev-keys/public.pem)|" .env
```

No macOS, o `sed` pede `-i ''`. Com o `.env` pronto, `docker compose up -d --build --wait` sobe o ambiente: o Compose grava a chave pública num volume e passa o caminho à API em `Authentication:LocalKey:PublicKeyPath`. Para rodar a API com `dotnet run`, sem o Compose, aponte essa chave de configuração para o `dev-keys/public.pem`, por exemplo com a variável de ambiente `Authentication__LocalKey__PublicKeyPath` e o caminho absoluto do arquivo.

O token de desenvolvimento é um JWT RS256 assinado com a chave privada, com emissor `https://idp.local.test`, audiência `ledger-api` e vida de uma hora, abaixo do teto de 240 minutos de `Development`:

```bash
b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }
now=$(date +%s)
header='{"alg":"RS256","typ":"at+jwt","kid":"dev-local"}'
payload='{"iss":"https://idp.local.test","aud":"ledger-api","sub":"dev-client","client_id":"dev-client","scope":"ledger.read ledger.write","iat":'$now',"nbf":'$now',"exp":'$((now+3600))',"jti":"'$(openssl rand -hex 16)'"}'
unsigned="$(printf '%s' "$header" | b64url).$(printf '%s' "$payload" | b64url)"
TOKEN="$unsigned.$(printf '%s' "$unsigned" | openssl dgst -sha256 -sign dev-keys/private.pem -binary | b64url)"
```

As chamadas levam `-H "Authorization: Bearer $TOKEN"`. Para um token só de leitura, basta tirar `ledger.write` do `scope`. Os exemplos de criar conta, lançar e consultar saldo e extrato estão em [Execução local](../10-implantacao-e-entrega/execucao-local.md).

## O cache de chaves do emissor

No modo `Authority`, o `JwtBearer` obtém os metadados e o JWKS do emissor, guarda as chaves públicas e as relê quando vê um `kid` desconhecido, no intervalo padrão da biblioteca de validação. Se o emissor cair depois da primeira leitura, os tokens de um `kid` já conhecido continuam valendo e a readiness não muda, porque ela não consulta o emissor. Um token de `kid` desconhecido recebe 401 `invalid_token` até o emissor voltar. O `JwksCacheTests` cobre esse comportamento contra um emissor simulado. O intervalo real de renovação com um emissor de verdade não foi medido ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)).

## O que fica registrado

Cada recusa gera uma linha de log e um incremento de `ledger_auth_failures_total`, com o rótulo `reason` de um conjunto fechado: `missing_token`, `invalid_token`, `expired`, `insufficient_scope`, `missing_client_id` e `invalid_client_id` ([Catálogo de métricas](../08-resiliencia-e-operacao/catalogo-de-metricas.md)). O evento 6001 (`AuthenticationRejected`) não leva `ClientId`, porque a claim de um token que não passou na validação não é confiável nem para o log, e o 6002 (`AuthorizationDenied`) leva.

Nunca entram em log, métrica ou trace o token, parte dele, o cabeçalho `Authorization` ou claims além de `client_id`. As falhas nas rotas anônimas não são contadas. A recusa de escrita por escopo, ou por cliente fora da lista de provisionamento, grava também `authorization.denied_write` na trilha, com o teto por chamador ([Trilha de auditoria](trilha-de-auditoria.md)). Os 401 e os 403 por falta de `client_id` ficam só em log e métrica.

## Testes

| Regra | Teste |
|---|---|
| Cada rota exige o escopo certo, e toda rota de negócio tem política | `RouteScopeMatrixTests`, `EndpointPolicyCoverageTests`, `ScopeAuthorizationHttpTests` |
| `WWW-Authenticate` sem o motivo da recusa | `ChallengeHeaderTests` |
| Motivos e métrica de falha | `AuthFailureMetricsTests` |
| Tolerância de 30 segundos, `alg` igual a `none`, token sem `exp` | `JwtValidationTests` |
| Validação com par de chaves assimétrico real | `RealKeyJwtTests` |
| Escopo parecido, claim vazia, `client_id` ausente ou inválido | `ScopeAuthorizationTests` |
| Criação de conta restrita à lista de provisionamento | `AccountProvisioningTests` |
| `LocalKey`, autoridade sem HTTPS e lista de provisionamento recusados fora de desenvolvimento | `ProductionGuardsTests` |
| Cache de chaves e emissor fora do ar | `JwksCacheTests` |
| Origem do chamador anônimo | `ForwardedHeadersTests` |
| Nenhum token em log | `AuthLogLeakTests` |
