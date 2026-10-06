# Decisões de arquitetura

Cada página deste diretório explica uma escolha de arquitetura do ledger: o problema que a motivou, o que foi decidido, o que foi descartado e por quê, e o que ficou pior por causa dela. O porquê mora aqui. O como está nas páginas de contrato e de fluxo, e o conjunto, na [Visão geral do ledger](../../01-visao-geral/visao-geral.md).

## O desenho em uma página

O ledger é imutável: só `INSERT` e `SELECT`, e o erro se corrige por estorno. Cada lançamento guarda `balance_after`, o que faz do saldo em qualquer instante uma busca por índice, e o banco decide o saldo com um `UPDATE` condicional por movimentação. Chave de idempotência, saldo, lançamento e evento entram no mesmo commit, e o Worker publica a caixa de saída no RabbitMQ com entrega ao menos uma vez. O acesso a dados é PostgreSQL com Npgsql, Dapper e migrações em SQL puro, sem cache de saldo, numa solução de monolito modular com dois executáveis. Para o porquê de cada frase, os documentos de arquitetura [0001](0001-monolito-modular-dois-executaveis.md) a [0009](0009-sem-cache-na-v1.md) e o [0013](0013-tempo-do-saldo-registrado-em-vs-ocorrido-em.md). O que ainda não foi medido está em [Limites conhecidos](../../09-qualidade/limites-conhecidos.md).

## Índice

| Número | Decisão |
|---|---|
| [0001](0001-monolito-modular-dois-executaveis.md) | Monolito modular com dois executáveis |
| [0002](0002-ledger-imutavel-somente-insercao.md) | Ledger imutável, somente inserção |
| [0003](0003-saldo-apos-em-cada-lancamento.md) | Saldo após em cada lançamento |
| [0004](0004-saldo-corrente-com-update-condicional.md) | Saldo corrente com update condicional |
| [0005](0005-transacao-unica-com-outbox.md) | Transação única com outbox |
| [0006](0006-idempotencia-por-chave-e-hash.md) | Idempotência por chave e hash |
| [0007](0007-postgresql-npgsql-dapper-dbup.md) | PostgreSQL, Npgsql, Dapper e DbUp |
| [0008](0008-rabbitmq-entrega-ao-menos-uma-vez.md) | RabbitMQ com entrega ao menos uma vez |
| [0009](0009-sem-cache-na-v1.md) | Sem cache na primeira versão |
| [0010](0010-seguranca-jwt-e-criptografia-de-pii.md) | Segurança com JWT e criptografia de dados pessoais |
| [0011](0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md) | Resiliência |
| [0012](0012-observabilidade-serilog-opentelemetry.md) | Observabilidade |
| [0013](0013-tempo-do-saldo-registrado-em-vs-ocorrido-em.md) | Tempo do saldo |
| [0014](0014-estrategia-de-testes.md) | Estratégia de testes |
| [0015](0015-sem-comentarios-e-zero-warnings.md) | Código sem comentários e build sem warnings |
| [0016](0016-azure-devops-como-plataforma-de-ci-cd.md) | CI/CD: Azure DevOps na entrega e GitHub Actions na validação |
| [0017](0017-retencao-de-35-dias-das-chaves-de-idempotencia.md) | Retenção de 35 dias das chaves de idempotência |
| [0018](0018-monotonicidade-do-recorded-at-e-janela-de-acomodacao.md) | Monotonicidade do `recorded_at` e janela de acomodação |
| [0019](0019-readiness-nao-depende-do-provedor-de-chaves.md) | A readiness não depende do provedor de chaves |
| [0020](0020-identificadores-uuid-v7.md) | Identificadores UUID v7 |
| [0021](0021-plataforma-dotnet-10.md) | Plataforma .NET 10 |
| [0022](0022-testes-dependentes-de-ambiente-falham-por-padrao.md) | Testes que dependem de ambiente falham por padrão |
| [0023](0023-client-id-no-hash-do-pedido.md) | O `client_id` entra no hash do pedido |
| [0024](0024-testes-de-unidade-da-infraestrutura-em-projeto-proprio.md) | Testes de unidade da infraestrutura em projeto próprio |
| [0025](0025-extrato-por-posicao-com-cursor-assinado.md) | Extrato por posição, com cursor assinado e limite superior único |
| [0026](0026-conferencia-de-integridade-com-trava-por-modo.md) | Conferência de integridade, com uma trava por modo |
| [0027](0027-limites-de-taxa-e-de-concorrencia-em-cadeia.md) | Limites de taxa e de concorrência em cadeia |
| [0028](0028-trilha-de-auditoria-com-catalogo-fechado.md) | Trilha de auditoria com catálogo fechado e teto na negação |
| [0029](0029-publicador-do-outbox-com-circuito-por-mensagem.md) | Publicador do outbox com circuito por mensagem e sonda própria |
| [0030](0030-prazo-proprio-para-marcar-as-mensagens-publicadas.md) | Prazo próprio para marcar e liberar as mensagens do lote |
| [0031](0031-criacao-de-conta-repetivel-com-identificador-previo.md) | Criação de conta repetível, com identificador gerado antes |
| [0032](0032-teste-fim-a-fim-que-provisiona-o-proprio-ambiente.md) | Teste fim a fim que provisiona o próprio ambiente |
| [0033](0033-fila-de-retencao-e-publicacao-mandatory.md) | Fila de retenção e publicação com `mandatory` |
| [0034](0034-suite-de-integracao-em-serie-e-perfil-rapido.md) | Suíte de integração em série e perfil rápido |
| [0035](0035-idempotency-key-opcional-na-criacao-de-conta.md) | `Idempotency-Key` opcional na criação de conta |
| [0036](0036-politica-de-fusos-horarios.md) | Política de fusos horários |
| [0037](0037-idioma-das-mensagens-ao-chamador.md) | Idioma das mensagens ao chamador |
