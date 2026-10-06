# Proteção de dados

O documento do titular (CPF ou CNPJ) é o único dado pessoal que o ledger guarda. A página mostra como ele é validado, mascarado, cifrado e indexado, como as chaves chegam ao processo e são trocadas, o que nunca entra em log, métrica, trace ou erro, e o que o sistema oferece para a LGPD. Descreve controles e não declara conformidade: base legal, prazos e atendimento ao titular são decisões do banco, e o que depende delas aparece como responsabilidade externa. O modelo de ameaças está em [Segurança](seguranca.md), e a criação de conta, em [Fluxo: criação de conta](../06-fluxos/criacao-de-conta.md).

## O que o ledger guarda e como cada dado é protegido

| Dado | Onde | Proteção |
|---|---|---|
| Documento do titular | `accounts.holder_document_encrypted` | AES-256-GCM no campo, com a conta como dado autenticado. Nunca devolvido em claro: a resposta traz só a máscara |
| Índice cego do documento | `accounts.holder_document_blind_index` | HMAC-SHA-256 com chave própria. Serve só à igualdade |
| Versão do conjunto de chaves | `accounts.holder_document_key_version` | Repete a versão que está dentro do blob e deve coincidir com ela, por restrição do banco |
| Valores, saldos e instantes dos lançamentos | `ledger_entries`, `account_balances` | Sem cifra por campo: o `UPDATE` do saldo e as consultas por `recorded_at` precisam enxergá-los, e o dado sozinho não aponta para ninguém |
| Texto livre do lançamento | `ledger_entries.description` e `reference` | Sem cifra, limitados a 140 e 100 caracteres, nunca logados nem usados em métrica, span ou evento. O contrato diz que o chamador não inclui dado pessoal neles |
| Identificação do chamador | `client_id` e `correlation_id` | Identificadores técnicos, sem dado pessoal |
| Chaves de cifra, do índice e do cursor | Fora do banco | Entregues ao processo pela plataforma, nunca gravadas em log, disco da aplicação ou imagem |

O ledger não recebe nome, e-mail, telefone, endereço nem data de nascimento: o corpo de `POST /v1/accounts` aceita só `holderDocument`, `currency` e `overdraftLimit`, e campo desconhecido é recusado. O vínculo entre uma conta e uma pessoa passa pelo documento cifrado ou pelo cadastro do banco, que não está aqui.

## O documento do titular

### Validação e máscara

A fábrica `HolderDocument.From` normaliza e valida, e o primeiro passo que falha recusa o documento com 400 `VALIDATION_FAILED` e razão `INVALID_FORMAT`, sem repetir o valor. Ela remove `.`, `-` e `/` e passa as letras para maiúsculas, o que precisa vir antes do cálculo porque o dígito do CNPJ usa o código do caractere menos 48. Aceita entrada de até 18 caracteres brutos e exige 11 caracteres (CPF, todos dígitos) ou 14 (CNPJ, com as 12 primeiras posições em dígitos ou letras de `A` a `Z` e as duas últimas em dígitos, o que cobre o CNPJ alfanumérico). Recusa documento de caracteres todos iguais, mesmo quando a conta fecha, e confere os dois dígitos verificadores. A posição dos separadores não é conferida, e `123456789.09` e `123.456.789-09` produzem o mesmo documento. `HolderDocumentTests` cobre formatos, separadores, letras, tamanhos e o limite de 18 caracteres.

Quando uma resposta precisa mostrar o documento, mostra a máscara de `HolderDocument.Masked()`, que deixa à vista no máximo três dígitos de um CPF e a ordem da filial de um CNPJ. Basta para quem opera confirmar que olha a conta certa, e não entrega o número. O `ToString()` do tipo devolve a própria máscara (`HolderDocumentMaskTests`).

| Documento | Normalizado | Máscara |
|---|---|---|
| CPF `123.456.789-09` | `12345678909` | `***.***.789-**` |
| CNPJ `12.345.678/0001-95` | `12345678000195` | `**.***.***/0001-**` |
| CNPJ alfanumérico `12.ABC.345/01DE-35` | `12ABC34501DE35` | `**.***.***/01DE-**` |

### Cifra

A coluna `holder_document_encrypted` (`bytea`) guarda um blob autodescritivo produzido por `AesGcmDocumentCipher`.

| Posição | Tamanho | Conteúdo |
|---|---|---|
| 0 | 1 byte | Versão do formato. `0x01` é AES-256-GCM com nonce de 96 bits e tag de 128 bits |
| 1 | 2 bytes | Versão do conjunto de chaves, inteiro sem sinal em big-endian |
| 3 | 12 bytes | Nonce aleatório, de `RandomNumberGenerator` |
| 15 | n bytes | Texto cifrado, do tamanho do documento normalizado (11 ou 14) |
| 15 + n | 16 bytes | Tag de autenticação |

Um CPF vira um blob de 42 bytes e um CNPJ, de 45. O primeiro byte permite trocar o algoritmo sem tornar ilegíveis os dados antigos (só existe o formato 1), e um blob de outro formato ou de tamanho inesperado é recusado antes de decifrar.

O dado autenticado associado são os três primeiros bytes do blob (formato e versão da chave) seguidos dos 16 bytes do `account_id` na ordem textual do GUID (a de `Guid.TryWriteBytes(span, bigEndian: true, out _)`, não a de `ToByteArray()`). Isso amarra o blob àquela linha: o mesmo conteúdo copiado para outra conta falha na decifragem, e trocar a versão da chave no cabeçalho também. É por isso que a aplicação gera o `account_id` antes do `INSERT`. Quando a autenticação falha, a decifragem devolve o erro `HOLDER_DOCUMENT_UNREADABLE`, sem lançar exceção e sem carregar chave ou documento na mensagem.

O nonce tem 96 bits sorteados a cada cifragem e nunca é derivado, contado nem recebido de fora, porque reutilizar um nonce com a mesma chave quebra o GCM por completo. Com nonce aleatório o limite prático é da ordem de 2^32 cifragens por chave, muito acima do número de contas do banco, e a rotação anual dá folga adicional. `NonceUniquenessTests` confere uma amostra de 100.000 cifragens com a mesma chave sem nonce repetido. `AesGcmDocumentCipherTests` cobre ida e volta, chave errada, conta trocada, alteração de cada parte do blob e tamanho inesperado, e `DocumentPayloadFormatTests` congela o formato com dois vetores publicados em `SecurityVectors`, um CPF e um CNPJ alfanumérico, conferindo o blob byte a byte contra uma cifra com nonce fixo.

### Índice cego

A busca por documento sem decifrar nada usa `holder_document_blind_index` (`bytea`, 32 bytes), que vale `HMAC-SHA-256(chave_do_índice, "holder-document:" + documento_normalizado)`, com o documento em UTF-8. O prefixo separa domínios, para a mesma chave nunca produzir o valor de outro uso. O índice da coluna é comum e parcial, não único, porque um titular pode ter mais de uma conta.

O HMAC com chave secreta é indispensável: um CPF tem cerca de um bilhão de valores, e um SHA-256 simples de entrada tão pequena seria revertido por força bruta em minutos a partir de um despejo do banco. Por isso o segredo do índice vale tanto quanto o da cifra, e as duas chaves precisam ser diferentes (o leitor de chaves recusa um conjunto com as duas iguais). O índice serve só à igualdade, sem busca por prefixo ou parte do número, e revela que duas contas têm o mesmo documento, embora não revele qual.

O documento fica cifrado, e não só o índice, por duas razões. Trocar a chave do índice exige o valor original para recalcular, e é o que a recifragem faz. E o direito de acesso do titular obriga a devolver o que o ledger guarda sobre ele. Se o jurídico concluir que nenhuma das duas se sustenta, guardar só o índice é uma simplificação possível sem mexer no resto do desenho.

O índice existe para o atendimento ao titular, mas nenhuma rota nem comando busca contas por documento. Quem o usa é a criação de conta com `Idempotency-Key`, que compara o índice do pedido com o de cada versão de chave viva. O hash do pedido guardado em `account_creation_keys.request_hash` leva o índice cego no lugar do documento, porque um SHA-256 simples do corpo seria reversível por força bruta e o índice, que depende da chave do cofre, não ([documento de arquitetura 0035](../03-principios-e-decisoes/documento-arquitetura/0035-idempotency-key-opcional-na-criacao-de-conta.md)). O `HmacBlindIndexTests` cobre vetores, determinismo, prefixo e diferença entre chaves.

## Chaves

### Conjunto, versão e provedores

A aplicação nunca lê chave de configuração diretamente. Pede ao `IKeyProvider`, porta da camada de aplicação, que devolve um `KeySet` com a versão e as chaves de cifra e de índice, ambas de 32 bytes. Várias versões podem estar vivas ao mesmo tempo, e a ativa (`Security:Pii:ActiveKeyVersion`, de 1 a 65.535, padrão 1) é a que cifra. As duas chaves de uma versão viajam juntas e cada uma serve a uma finalidade só. A versão fica no blob e se repete em `holder_document_key_version`, para a recifragem achar as linhas antigas por um índice simples. O blob é a fonte da verdade, e a restrição `ck_accounts_holder_document_key_version_matches_payload` obriga as duas a concordarem: sem ela, uma linha em que divergissem sumiria do filtro da recifragem e ficaria ilegível no dia em que a versão antiga saísse da lista de vivas.

O provedor `Configuration` (`Security:Pii:KeySets:<versão>:EncryptionKey` e `BlindIndexKey`, em base64) serve a desenvolvimento e teste, e a subida fora de `Development` e `Testing` com ele é recusada. O provedor `Directory` é o de produção e lê pela porta `IKeySetSource`, cuja única implementação é `DirectoryKeySetSource`: `<Security:Pii:Directory>/<versão>/encryption.key` e `blind-index.key`, em base64, numa pasta que a plataforma monta em memória a partir do cofre. Não existe adaptador que fale com a API de um cofre. O `ReloadingKeyProvider` carrega todas as versões vivas na subida e relê tudo a cada `Security:Pii:ReloadMinutes` (padrão 10), trocando o retrato inteiro de uma vez, de modo que uma rotação é percebida sem reinício. Que a degradação com o cofre fora (criação de conta com 503, readiness `Degraded`, lançamentos e consultas intactos) se comporte em produção como nos testes, com um provedor que falha, ainda não foi verificado contra um cofre real ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)).

### Subida, recarga e falhas

O desenho distingue indisponibilidade de defeito: cofre fora é indisponibilidade, chave errada é defeito.

| Situação | Na subida | Na recarga |
|---|---|---|
| Fonte inalcançável (pasta ausente, vazia ou sem permissão) | O processo sobe sem chaves. A readiness da API fica `Degraded` e a criação de conta responde 503 `SERVICE_UNAVAILABLE`. Lançamentos, saldos e extratos não usam chave e seguem | O retrato anterior permanece, e a falha é registrada (`KeyReloadFailed`) e contada |
| Material malformado: chave de tamanho diferente de 32 bytes, as duas chaves iguais, base64 inválido ou duas pastas da mesma versão | O processo não sobe. A mensagem nomeia a chave e não imprime valor | O retrato novo é descartado, o anterior permanece e o erro vai a `Error` (`KeyMaterialRejected`) |
| `ActiveKeyVersion` sem conjunto correspondente, ou a cifragem e a decifragem de verificação falhando | O processo não sobe | O retrato novo é descartado |
| Uma versão já carregada volta com chave diferente | Não se aplica | O retrato novo é descartado: uma versão nunca muda de conteúdo |
| Uma versão carregada some da fonte | Não se aplica | O retrato novo é adotado, `KeyVersionsVanished` é registrado e a readiness da API fica `Degraded` até a versão voltar ou o processo reiniciar |
| `Provider` igual a `Configuration` fora de `Development` e `Testing` | O processo não sobe | Não se aplica |

A readiness da API inclui o provedor como `Degraded`, que responde 200 com alerta e não 503, porque as chaves só servem à criação de conta e um cofre fora do ar não pode tirar do balanceamento instâncias que registram lançamentos sem tocar em chave ([documento de arquitetura 0019](../03-principios-e-decisoes/documento-arquitetura/0019-readiness-nao-depende-do-provedor-de-chaves.md)). A verificação `keys` só existe na API. O Worker usa o mesmo provedor para recifrar e, se a fonte falhar, o efeito aparece nas métricas e no batimento do laço dele. O `KeyProviderStartupGuard` ainda faz uma cifragem e uma decifragem de verificação com um documento fixo, de modo que uma chave truncada ou uma permissão faltando no cofre derrubam a subida com mensagem clara em vez de falhar na primeira conta criada. Os testes, todos em `Ledger.Infrastructure.Tests`, são `ReloadingKeyProviderTests`, `DirectoryKeySetSourceTests`, `ConfigurationKeyProviderTests`, `KeyProviderStartupGuardTests`, `KeyProviderHealthCheckTests` e `PiiOptionsValidationTests`.

### Gestão em produção

O código define como as chaves são lidas, validadas e trocadas. O resto é o que a implantação precisa fazer, e o repositório não o exercita. Quem tem o dump do banco e as chaves lê todos os documentos, e por isso as chaves vivem em outro sistema, com outro controle de acesso. A custódia fica num cofre gerenciado do banco, com identidade de carga de trabalho só de leitura para a API e para o Worker, nenhuma pessoa lendo a chave em operação normal e uma cópia de recuperação em custódia dividida, com dois guardiões. As chaves chegam pela pasta montada em memória, um subdiretório por versão, geradas em duplas de 32 bytes aleatórios no cofre ou em cerimônia registrada e liberadas para a API e o Worker antes de ficarem ativas. A rotação é anual, ou na hora em caso de suspeita. O acesso é auditado pelo cofre, porque o ledger não registra o acesso ao arquivo. A versão retirada continua no cofre pelo tempo de retenção dos backups, porque um backup restaurado traz blobs cifrados com ela. E a perda total das chaves deixa os documentos ilegíveis sem afetar saldos nem lançamentos, que é o motivo da cópia de recuperação.

## Rotação e recifragem

A rotação planejada troca a versão ativa e recifra os documentos sem parar o serviço. O detalhe de execução está em [Fluxo: rotação das chaves de dados pessoais](../06-fluxos/rotacao-de-chaves.md), e aqui ficam os passos e as condições de segurança.

1. Gerar o conjunto da versão N+1 no cofre e liberá-lo para a API e o Worker sem torná-lo ativo. Em até `ReloadMinutes` os processos o carregam como versão viva.
2. Tornar N+1 a ativa (`Security:Pii:ActiveKeyVersion`) e reiniciar as instâncias. Contas novas usam N+1, e as antigas continuam legíveis porque o provedor ainda entrega N. O Worker registra a ativação (`keys.version_activated`) na primeira passada depois da subida, uma vez só.
3. O Worker recifra, em lotes (`Security:Pii:Rewrap`), as contas com `holder_document_key_version` menor que a ativa: decifra com a versão antiga, cifra com a nova, recalcula o índice cego e atualiza a linha, e na mesma transação grava `pii.decrypted` (contagem e finalidade `rewrap`) e `pii.rewrapped` (versões de origem e destino, recifrados e falhas) em `audit_log`. Duas passadas não rodam ao mesmo tempo. A tabela `accounts` não é imutável, e esse `UPDATE` é legítimo: o papel `ledger_worker` só altera as três colunas do documento.
4. Durante a janela, a repetição da criação de conta calcula o índice com todas as versões vivas e aceita o pedido se algum casar, então a rotação não transforma uma repetição legítima em 422.
5. A versão N só sai da lista de vivas quando três condições valem juntas: a passada relata zero contas abaixo da ativa e nenhuma numa versão desconhecida (`ledger_pii_accounts_below_active_key` chega a zero); todas as instâncias da API já rodam com N+1 ativa, porque uma que ainda grave sob N depois do zero cria linhas que a passada só veria na seguinte; e passaram 35 dias da última conta criada com chave de idempotência sob N, o prazo em que o chamador ainda pode repetir o pedido. Antes disso uma repetição legítima receberia 422 em vez da conta original, sem criar conta duplicada.

A retirada de uma versão deixa a readiness da API em `Degraded` até as instâncias reiniciarem, porque o provedor registra a versão que sumiu e só esquece quando o processo recomeça. O sinal é esperado numa retirada planejada. Sob suspeita de vazamento o procedimento é o mesmo sem esperar o ciclo anual: rotação de emergência, recifragem com prioridade e a versão comprometida fora da lista assim que a passada converge. Os backups com blobs sob a chave comprometida continuam expostos até expirarem, um limite real da cifra por campo que o código não resolve. Os testes são `RewrapConvergenceTests` (convergência, blob adulterado sem laço, dois Workers sem trabalho duplicado), `KeyRewrapServiceTests`, `PostgresAccountKeyRewrapperTests` e `KeyActivationAuditTests`.

## O que nunca entra em log, métrica, trace ou erro

O documento em claro só existe no corpo da requisição que o recebe e na memória do processo durante o tratamento, e os buffers de bytes com o texto claro da cifragem são zerados logo depois do uso. O corpo das requisições nunca é logado, então o campo não chega ao log mesmo que alguém esqueça de marcá-lo. Os mecanismos abaixo se somam.

| Mecanismo | O que faz | Testes |
|---|---|---|
| `[Sensitive]` e nomes de propriedade | `SensitiveDataDestructuringPolicy` troca por `***` o valor de membro marcado com `[Sensitive]` ou cujo nome contenha `password`, `secret`, `token`, `authorization`, `connectionstring`, `apikey`, `document`, `cpf`, `cnpj`, `idempotencykey`, `encryptionkey` ou `blindindex` | `SensitiveDataDestructuringPolicyTests` |
| Mascaramento de texto | `SensitiveValueMaskingEnricher` aplica `SensitiveTextMasker` a toda propriedade de texto do log: `Bearer` com token, JWT, `password=`, `secret=`, `api_key=`, CPF e CNPJ formatados e texto que seja só um número de 11 ou 14 dígitos | `SensitiveValueMaskingEnricherTests`, `SensitiveTextMaskerTests` |
| Tipos que não imprimem segredo | `KeySet`, `ProtectedHolderDocument` e `RawKeySet` só imprimem a versão no `ToString()`, e `HolderDocument` imprime a máscara | `ReloadingKeyProviderTests.KeySet_ToString_PrintsOnlyTheVersion`, `HolderDocumentProtectorTests.ProtectedDocument_ToString_PrintsOnlyTheKeyVersion` |
| Erros sem eco | Problem Details com `detail` fixo por código e, na validação, o campo, a razão e a regra violada, sem pilha nem o valor recebido. `Postgres:IncludeErrorDetail` fica desligado fora de desenvolvimento, porque o detalhe do PostgreSQL pode conter valores de linha | `ProblemCatalogTests`, `LogLeakTests`, `PostgresOptionsValidatorTests` |
| Chave de idempotência | O log traz só uma impressão digital (4 primeiros bytes do SHA-256, em 8 hexadecimais), porque chamadores compõem a chave com identificadores de negócio | `EntryLogLeakTests` |
| Rótulos de métrica | O `client_id` só vira rótulo sanitizado (até 64 caracteres, alfabeto restrito), com no máximo 128 valores distintos, e o resto vira `other` ou `unknown` | `ClientLabelsTests` |
| Atributos de span | Só tipos simples, nenhum valor monetário e nenhum valor sentinela de teste | `SpanAttributesPrivacyTests` |
| Auditoria e mensageria | Os `details` da trilha usam chaves fechadas, sem documento, blob nem índice, e `EntryRegistered` não carrega descrição, referência nem documento | `AuditDetailsPrivacyTests`, `EntryRegisteredPayloadTests` |

Uma família de testes cria contas, provoca erros e procura valores sentinela em tudo o que sai: `LogLeakTests`, `AuthLogLeakTests`, `ReadLogLeakTests`, `WriteLogLeakTests` e `AccountDocumentProtectionTests`, que confere no banco que só existem o blob e o índice e nunca o documento, e que o blob copiado para outra conta ou adulterado não decifra.

## Dados em trânsito e em repouso

Fora de `Development` e `Testing`, o TLS termina na borda, e o trecho da borda até o pod é exigência de infraestrutura, porque a API escuta HTTP e não sabe se ele foi cifrado ([Segurança](seguranca.md)). A subida exige `Postgres:SslMode` em `VerifyFull` e `RabbitMq:UseTls` ligado, e o TLS efetivo nos dois ainda não foi exercitado contra uma infraestrutura real ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)). O JWKS exige HTTPS (`RequireHttpsMetadata`), e o OTLP deve ir sobre TLS, o que depende da implantação. Em desenvolvimento e teste tudo é HTTP e `amqp://` na rede do Compose.

Em repouso, o disco do PostgreSQL e os backups devem ser cifrados pela infraestrutura, e o repositório não tem como verificar isso. Esse controle protege contra perda física de mídia, não contra um operador com acesso lógico ao banco, e para esse caso existe a cifra por campo, que cobre só o documento.

## LGPD

O que segue é o que a engenharia precisa saber para o ledger não criar problema e para as respostas aos titulares serem possíveis. O raciocínio jurídico é do encarregado de dados e do jurídico do banco, e as indicações abaixo são subsídio, não parecer.

O documento do titular é dado pessoal direto. O CNPJ de pessoa jurídica não o é em sentido estrito, mas os dois recebem o mesmo tratamento, porque separar o MEI do empresário individual pelo número não é confiável. O `account_id` e os lançamentos são dado pessoal indireto: sozinhos não identificam ninguém, mas o cadastro do banco os liga a uma pessoa, e por isso todo o conjunto segue as regras de acesso, retenção e resposta a pedidos. A minimização mantém a superfície pequena: nada de nome, contato ou cadastro, lançamentos sem documento, e só o documento do titular, cifrado.

A base legal que a engenharia assume, a confirmar antes da produção, é esta. Registrar e consultar movimentações é execução do contrato da conta (art. 7º, V). Guardar os lançamentos por dez anos é obrigação legal ou regulatória (art. 7º, II), com o prazo como premissa do projeto (BR-18), coerente com a prescrição geral do Código Civil (art. 205) e com a guarda de registros da Circular BCB 3.978/2020, de prevenção à lavagem de dinheiro. Logs de segurança se apoiam no legítimo interesse (art. 7º, IX). O sigilo bancário (Lei Complementar 105/2001) pesa sobre saldo e extrato e justifica o controle por escopo e o rastro de quem consultou (BR-19 e BR-20). O compliance confirma quais normas se aplicam ao banco.

### Retenção

No código, só as chaves de idempotência e o outbox publicado têm rotina de remoção. Nas demais tabelas, o que o sistema garante é a ausência de remoção. Os prazos de logs, traces e backups são os que o desenho assume para a plataforma, e o repositório não os impõe.

| Dado | Prazo | Como se aplica |
|---|---|---|
| `ledger_entries` | 10 anos, sem remoção física | Nenhum SQL remove lançamento. O destino depois dos 10 anos é pergunta em aberto |
| `accounts` e `holder_document_*` | Enquanto a conta existir, mais 10 anos depois do encerramento | O sistema não tem estado de conta encerrada, então nada aplica esse prazo |
| `idempotency_keys` e `account_creation_keys` | 35 dias, de `created_at` | Worker, por `Idempotency:RetentionDays` |
| `outbox_messages` publicadas | 7 dias depois da publicação | Worker, por `Outbox:RetentionDays` |
| `audit_log` | 10 anos | Nenhuma rotina remove linha, e o prazo é o da premissa regulatória |
| Logs de aplicação | 90 dias pesquisáveis | Plataforma de logs |
| Logs de segurança e de auditoria de leitura | 1 ano em armazenamento frio | Plataforma de logs |
| Traces | 7 dias | Plataforma de telemetria |
| Backups | 35 dias | Plataforma de armazenamento |

Enquanto o jurídico não decidir se os dez anos valem para o documento ou só para os lançamentos, o desenho mantém o documento durante a retenção, porque sem ele a conta fica sem vínculo e não se atende uma ordem judicial sobre um titular específico.

### Direitos do titular

O pedido chega pelo canal de atendimento do banco, nunca direto ao ledger. A tabela diz o que o sistema oferece hoje para cada direito do art. 18.

| Direito | O que o ledger oferece | O que falta ou depende do banco |
|---|---|---|
| Confirmação do tratamento e localização das contas de um documento | O índice cego permite a busca por igualdade, mas nenhuma rota nem comando a faz | Faltaria um comando administrativo do Worker que calcula o índice em memória, sem a chave sair do processo, e deixa registro na trilha. Uma consulta SQL manual não calcula o HMAC sem expor a chave a quem opera |
| Acesso aos dados | O extrato, por `GET /v1/accounts/{accountId}/entries` | A devolução do documento em claro não tem caminho. Exigiria procedimento administrativo auditado, que não existe |
| Correção | Nenhuma rota altera o documento de uma conta | Exigiria cifrar o documento corrigido com a versão ativa, procedimento do banco com registro na trilha |
| Portabilidade | O mesmo extrato, em JSON | Formato e canal de entrega são do banco |
| Informação sobre compartilhamento | Os eventos do outbox vão a consumidores internos e não carregam dado pessoal. O ledger não compartilha dado com terceiros | A lista de consumidores é do banco |
| Eliminação e anonimização | Nenhum caminho remove lançamento | O conflito com a retenção legal, a seguir |

### Eliminação e retenção legal

Um titular pode pedir a exclusão dos dados, e os lançamentos são imutáveis por desenho e guardados por dez anos por obrigação legal. O art. 16 autoriza conservar dados para cumprir obrigação legal, então a resposta recomendada durante o prazo é recusar a eliminação dos lançamentos e explicar o motivo, decisão do encarregado. A minimização reduz o atrito: os lançamentos não carregam identificação, e manter o histórico financeiro não mantém, por si só, o documento.

Depois do prazo, a recomendação é anonimizar a conta em vez de apagar lançamentos: zerar `holder_document_encrypted`, `holder_document_blind_index` e `holder_document_key_version`, que aceitam `NULL` juntas por restrição (`ck_accounts_holder_document`). Sem o documento e sem o cadastro externo, conta e lançamentos deixam de identificar uma pessoa. O sistema não traz rotina que faça isso, e quem a executa e quando é decisão do banco. Se o jurídico exigir a exclusão física dos lançamentos, o caminho é particionar a tabela por tempo e descartar partições inteiras ([Evolução futura](../11-evolucao/evolucao-futura.md)). O dado apagado continua nos backups até eles expirarem, e a resposta ao titular precisa dizer isso. Uma chave por conta resolveria melhor, porque destruí-la apagaria o dado também nos backups, mas multiplicaria as chaves a gerir e não existe. O destino dos dados depois dos dez anos está em [Questões em aberto](../03-principios-e-decisoes/questoes-em-aberto.md).

### Incidentes

A comunicação à ANPD e aos titulares é processo do banco, no prazo regulamentado, hoje de três dias úteis para incidentes de risco relevante, que o encarregado confirma. O que o ledger oferece nesse momento é limitado. A trilha registra cada lote de recifragem com a contagem de documentos decifrados, mas não identifica as contas: mostra que o Worker decifrou e quantos, não quais. Como a recifragem é o único caminho do código que decifra documentos reais, qualquer acesso fora dela só deixa rastro na plataforma de segredos e nos logs do banco, e por isso o acesso às chaves precisa ser auditado fora do ledger. Para saber quais contas estão sob uma chave comprometida, vale a consulta por `holder_document_key_version` antes da recifragem, e a rotação de emergência encerra a exposição futura.
