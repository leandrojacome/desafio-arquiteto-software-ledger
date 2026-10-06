# Glossário

A documentação está em português e o sistema, em inglês. Cada linha liga o termo da prosa ao nome físico, que aparece em fonte de código nas demais páginas, e se uma página divergir do código num nome físico, vale o código. O que uma pessoa lê na resposta da API (o `title`, o `detail` e a `message` de cada campo) está em português e usa estes termos. O que um programa lê, como os códigos de erro e os nomes de campo, está em inglês.

## Negócio

| Termo | Significado | Nome no sistema |
|---|---|---|
| Ledger | O livro-razão das contas dos clientes. Não é a contabilidade geral do banco | Solução `Ledger.sln` e projetos `Ledger.*` |
| Conta | Unidade que acumula lançamentos, com uma moeda, um saldo e um limite de cheque especial. Nasce em outro sistema, e o ledger guarda um cadastro mínimo | Tabela `accounts`, tipo `AccountId` |
| Lançamento | Um crédito ou um débito de valor positivo em uma conta. Depois de confirmado, nunca muda | Tipo `Entry`, tabela `ledger_entries` |
| Crédito e débito | Os dois tipos de lançamento: o primeiro soma ao saldo, o segundo subtrai | `EntryType.Credit` e `EntryType.Debit`, coluna `type` com `CREDIT` e `DEBIT` |
| Estorno | Lançamento de tipo oposto e mesmo valor que corrige outro sem alterá-lo. Cada lançamento é estornado no máximo uma vez, e um estorno não é estornável | Coluna `reverses_entry_id`, rota `POST /v1/accounts/{accountId}/entries/{entryId}/reversals` |
| Saldo atual | Quanto a conta tem agora | Tabela `account_balances`, coluna `balance` |
| Saldo após o lançamento | O saldo que a conta passou a ter logo depois do lançamento. Permite fechar o extrato sem somar o histórico | Coluna `balance_after` |
| Limite de cheque especial | Quanto a conta pode ficar abaixo de zero. O padrão é zero | Coluna `overdraft_limit` |
| Moeda | Código de três letras, igual ao da conta. A primeira versão aceita só BRL, o real | Coluna `currency` |
| Titular e documento | A pessoa dona da conta e seu CPF ou CNPJ. O ledger não recebe nem guarda o nome do titular | Tipo `HolderDocument`, colunas `holder_document_encrypted`, `holder_document_blind_index` e `holder_document_key_version` |
| Chamador | O sistema do banco que chama a API, identificado pelo token e gravado em cada lançamento | Claim e coluna `client_id` |
| Escopo | Permissão do token: `ledger.read` autoriza consultas, `ledger.write` autoriza criar conta, lançar e estornar | Claim `scope`, políticas `ledger.read` e `ledger.write` |

## Tempo e saldo

| Termo | Significado | Nome no sistema |
|---|---|---|
| Momento do registro | Instante em que o ledger gravou o lançamento, pelo relógio do banco. É ele que define o saldo em um instante | Coluna `recorded_at` |
| Data de negócio | Instante que o chamador informa para o lançamento. É guardada em UTC e aparece no extrato, mas não entra no saldo. Se omitida, vale o momento do registro | Coluna `occurred_at`, campo `occurredAt` |
| Versão da conta | Posição do lançamento na sequência da conta, a partir de 1 e sem lacunas | Coluna `account_version`, campo `accountVersion` |
| Saldo em um instante | O `balance_after` do último lançamento com `recorded_at` menor ou igual ao instante pedido. Antes do primeiro lançamento, zero | Rota `GET /v1/accounts/{accountId}/balance?asOf=` |
| Instante | Ponto na linha do tempo, em ISO 8601 com fuso horário. É a palavra que a documentação e as mensagens usam no lugar de "data e hora" | Campos `occurredAt`, `recordedAt`, `asOf`, `from` e `to` |
| Fuso horário | O deslocamento em relação ao UTC (`Z` ou `±HH:MM`), obrigatório em todo instante de entrada. A API converte para UTC, e a saída leva sempre o sufixo `Z` | Colunas `timestamptz`, razão `MISSING_TIME_ZONE` |
| Horário de Brasília | UTC-3 o ano todo, porque o Brasil não tem horário de verão desde 2019. É o fuso do dia civil do negócio | Deslocamento `-03:00` |
| Janela de acomodação | Intervalo recente (5 segundos por padrão) em que um lançamento já pode ter recebido o `recorded_at` sem ter confirmado. Dentro dela, o saldo em um instante ainda pode mudar | `Ledger:Balance:SettlingWindowSeconds` |
| `settled` | Campo da resposta do saldo em um instante. Verdadeiro quando o instante saiu da janela e a resposta não muda mais | Campo `settled` |
| Extrato | Lançamentos de uma conta, do mais recente ao mais antigo, em páginas, com período opcional (`from` inclusivo, `to` exclusivo) | Rota `GET /v1/accounts/{accountId}/entries` |
| Cursor | Marca opaca e assinada que diz de onde a próxima página do extrato continua, amarrada à conta | Campo `nextCursor`, tipo `HmacStatementCursorProtector` |

## Respostas da API

| Termo | Significado | Nome no sistema |
|---|---|---|
| Requisição | A chamada HTTP do chamador. A documentação também diz pedido | Corpo, cabeçalhos e parâmetros de consulta |
| Erro por campo | Um item da lista de problemas de uma validação: campo, razão e mensagem. "Campo" vale para propriedade do corpo, parâmetro de consulta e cabeçalho | Lista `errors`, com `field`, `reason` e `message` |
| Razão | Código estável, em inglês, da regra de validação que falhou | `reason`, como `TOO_MANY_DECIMALS` |
| Código de erro | Identificador estável, em inglês, de cada erro do catálogo. O chamador decide por ele e pelo status HTTP, nunca pela frase | `code`, como `INSUFFICIENT_FUNDS` |
| Cota de requisições | Limite por chamador e por conta. Ao exceder, a resposta é 429 com o intervalo para tentar de novo | Seção `RateLimiting`, erro `RATE_LIMITED`, cabeçalho `Retry-After` |
| Problem Details | Formato padronizado das respostas de erro, com `code` estável, título e detalhe em português | Tipo `ProblemCatalog` |

## Idempotência e eventos

| Termo | Significado | Nome no sistema |
|---|---|---|
| Chave de idempotência | Valor escolhido pelo chamador, uma vez por intenção de negócio. Repetir a chave com o mesmo conteúdo devolve o resultado original | Cabeçalho `Idempotency-Key`, tabelas `idempotency_keys` e `account_creation_keys` |
| Hash canônico | SHA-256 do conteúdo normalizado do pedido, chamador incluído. Distingue repetição de reutilização indevida da chave | Tipo `CanonicalRequestHash`, colunas `request_hash` e `hash_version` |
| Repetição | Pedido repetido com a mesma chave e o mesmo conteúdo. A resposta é a original e traz o cabeçalho que a identifica | `Idempotent-Replayed: true` |
| Caixa de saída (outbox) | Tabela em que cada evento é gravado na transação do lançamento, para ser publicado depois | Tabela `outbox_messages` |
| Evento `EntryRegistered` | A mensagem de negócio publicada para cada lançamento confirmado, estornos inclusive | Tipo `EntryRegisteredPayload`, campo `eventId` |
| Entrega pelo menos uma vez | O evento chega ao menos uma vez e pode chegar repetido. O consumidor deduplica pelo `message_id`, igual ao `eventId` do corpo | Propriedade `message_id` da mensagem AMQP |
| Fila de retenção | Fila em que os eventos esperam até algum consumidor declarar a sua | `ledger.events` (exchange) e `retention.ledger.entry-registered` |
| Circuit breaker | Interrompe as tentativas de publicação quando o broker falha de forma repetida e as retoma depois de um tempo | Seção `Resilience:BrokerCircuitBreaker` |

## Segurança e dados pessoais

| Termo | Significado | Nome no sistema |
|---|---|---|
| JWT e client credentials | Formato do token e fluxo em que um sistema o obtém com a própria credencial, sem usuário | Seção `Authentication` |
| Índice cego | HMAC do documento normalizado, que permite buscar por documento sem guardá-lo em claro | Coluna `holder_document_blind_index`, tipo `HmacBlindIndex` |
| Documento cifrado | O documento cifrado com AES-256-GCM, com a conta como dado associado | Coluna `holder_document_encrypted`, tipo `AesGcmDocumentCipher` |
| Versão da chave | Número que identifica o conjunto de chaves que cifrou um documento e permite trocar a chave sem parar o serviço | Coluna `holder_document_key_version`, chave `Security:Pii:ActiveKeyVersion` |
| Provedor de chaves | Componente que entrega as chaves de dados pessoais ao processo | Interface `IKeyProvider` |
| Mascaramento | Documento com os dígitos trocados por asteriscos, exceto os que identificam sem revelar | Método `HolderDocument.Masked` |
| Trilha de auditoria | Registro imutável de ações administrativas e de segurança, com catálogo fechado de tipos | Tabela `audit_log`, tipo `AuditEventTypes` |
| Papel de banco | Usuário do PostgreSQL com o privilégio mínimo de uma função | `ledger_migrator`, `ledger_api`, `ledger_worker` e `ledger_readonly` |

## Operação

| Termo | Significado | Nome no sistema |
|---|---|---|
| Worker | O executável das tarefas de fundo: publica o outbox, confere a integridade, poda, mede o acúmulo e recifra documentos | Projeto `Ledger.Worker` |
| Conferência de integridade | Verificação periódica de que o saldo de cada conta corresponde aos seus lançamentos. Avisa e nunca corrige | Tipo `RunIntegrityCheckHandler`, seção `Integrity` |
| Liveness e readiness | Sondas de saúde: a primeira diz se o processo está de pé, a segunda se ele pode receber tráfego | `GET /health/live` e `GET /health/ready` |
| Identificador de correlação | Valor que acompanha a requisição nos logs, nos traces, na auditoria e nos eventos | Cabeçalho `X-Correlation-Id`, coluna `correlation_id` |
| RPO e RTO | Perda de dados e tempo de recuperação aceitáveis depois de uma falha grave | Metas NFR-06 e NFR-07 |
| SLI, SLO e p99 | Indicador de nível de serviço, o objetivo para ele e o valor abaixo do qual ficam 99% das amostras | [Indicadores, objetivos e alertas](../08-resiliencia-e-operacao/slos-e-alertas.md) |

## Leia também

- [Modelo de dados](../05-contratos/modelo-de-dados.md): as tabelas, colunas e privilégios citados aqui.
- [Contrato da API REST](../05-contratos/api-rest.md): rotas, cabeçalhos e a regra de datas e fusos.
- [Catálogo de erros](../05-contratos/catalogo-de-erros.md): o texto de cada mensagem que usa estes termos.
