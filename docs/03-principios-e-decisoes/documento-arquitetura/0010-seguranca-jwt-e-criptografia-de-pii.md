# Documento de arquitetura 0010: Segurança com JWT e criptografia de dados pessoais

## Contexto

Quem chama o ledger são outros sistemas do banco, não um usuário com sessão. Mesmo assim o risco é alto: a API move dinheiro e guarda o documento do titular (CPF ou CNPJ), dado pessoal sob a LGPD. É preciso autenticar máquinas, separar quem lê de quem escreve, proteger o documento em repouso sem perder a busca por ele e não vazar nada disso em log.

## Decisão

A autenticação é OAuth 2.0 com client credentials. O provedor de identidade do banco emite JWTs assinados e a API os valida localmente: assinatura pelas chaves públicas publicadas (JWKS, com cache), `iss`, `aud`, `exp` e `nbf`, com tolerância de relógio de 30 segundos. Rotas `GET` exigem o escopo `ledger.read` e rotas `POST` exigem `ledger.write`, e a criação de conta exige ainda que o `client_id` conste na lista de provisionamento. A política padrão é negar: todo endpoint nasce exigindo autenticação, e só `/health/live` e `/health/ready` são anônimos, por declaração explícita.

A API recusa o token cuja vida passa de 15 minutos e o que não tem o `typ` `at+jwt`, de modo que um emissor desviado para tokens longos aparece como `invalid_token` na métrica de falhas de autenticação, em vez de ser obedecido em silêncio. Fora do Compose local, o tráfego usa TLS 1.2 ou superior, e a conexão com o PostgreSQL exige `VerifyFull`.

O documento do titular é cifrado na aplicação com AES-256-GCM, nonce aleatório de 12 bytes por operação e o identificador da conta como dado autenticado adicional, de modo que um texto cifrado copiado para outra linha falha na verificação. O valor gravado leva a versão do formato e a da chave, o que permite rotacionar. Para achar uma conta pelo documento existe um índice cego: HMAC-SHA-256 do documento normalizado, com chave diferente da de cifragem. Um CPF tem cerca de um bilhão de combinações, e um SHA-256 simples seria revertido em minutos por quem tivesse a tabela.

As chaves chegam por configuração ou por pasta de segredos, nunca no código nem na imagem. O documento e o corpo das requisições não vão a log, e o Serilog mascara o que escapar ([documento de arquitetura 0012](0012-observabilidade-serilog-opentelemetry.md)). A trilha de auditoria registra `client_id`, rota, conta e resultado ([documento de arquitetura 0028](0028-trilha-de-auditoria-com-catalogo-fechado.md)), todo SQL é parametrizado e o papel do banco usado pela aplicação tem o menor privilégio possível.

## Alternativas descartadas

- Chave de API estática por chamador. Não expira, não carrega escopo e a rotação é dolorosa.
- mTLS como único mecanismo. Não carrega escopo, e operar certificados por chamador pesa mais que usar o provedor de identidade do banco. Pode ser somado na borda.
- Tokens opacos com introspecção. Dão revogação imediata, mas põem o provedor no caminho crítico de cada requisição.
- `pgcrypto` no banco. A chave e o texto claro passam pelo SQL e podem parar em `pg_stat_statements` e nos logs do servidor.
- Só criptografia de disco. Não protege contra quem lê a tabela (administrador, backup vazado, injeção de SQL). Fica como complemento.
- AES-SIV no lugar do índice cego. O .NET não traz SIV, e dependeria de biblioteca de terceiros em criptografia.

## Consequências

Um token revogado vale até expirar, no máximo 15 minutos. A saída de emergência para um cliente comprometido é desativá-lo no emissor e esperar a janela.

Os escopos são grossos: quem tem `ledger.write` lança e estorna em qualquer conta, e também descobre o saldo dela, porque o `balanceAfter` dos lançamentos aceitos e as recusas por saldo permitem inferi-lo por tentativas. O ledger oferece detecção, com alertas sobre a taxa de `INSUFFICIENT_FUNDS` e de estornos por `client_id` ([SLOs e alertas](../../08-resiliencia-e-operacao/slos-e-alertas.md)), e não bloqueio. Para sistemas internos é aceitável na primeira versão, e o risco é contido pela vida curta do token, pelo limite de taxa por cliente ([documento de arquitetura 0011](0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md)), pela auditoria e pela imutabilidade, já que um lançamento indevido só se corrige com um estorno visível.

O índice cego só atende igualdade. Rotacionar a chave de cifragem é recifrar em lote, mas rotacionar a chave do HMAC obriga a recalcular o índice inteiro. Se o banco passar a exigir revogação imediata de token, autorização por conta ou tokenização por cofre próprio, esta decisão precisa ser refeita.
