# Questões em aberto

Perguntas que o desenho deixa sem resposta de negócio ou de operação. Cada uma traz a recomendação que o sistema adota enquanto ninguém responde e o que muda se a resposta for a contrária. Nenhuma bloqueia a primeira versão. O que se resolve por medida, e não por decisão, está em [Limites conhecidos](../09-qualidade/limites-conhecidos.md).

## Produto e regras de negócio

### Qual é a linha de base do ledger anterior

Medir latência, taxa de erro e volume em pico por duas semanas, incluindo um dia de pagamento, antes de fechar as metas. Se os números reais forem muito diferentes das premissas, o dimensionamento muda, e a arquitetura provavelmente não.

### O estorno parcial entra

Não na primeira versão: a devolução parcial é um novo crédito com referência ao lançamento de origem. Se a resposta for sim, cai a regra de um estorno por lançamento, hoje imposta pelo índice único `uq_ledger_entries_reverses_entry_id`, e o contrato do estorno precisa registrar o valor já estornado.

### O que fazer com o estorno de um crédito já gasto

Recusar, como na BR-05: o estorno passa pela regra de saldo de qualquer lançamento e recebe 422 `INSUFFICIENT_FUNDS`, e o crédito indevido já gasto fica com o processo de cobrança do banco. Se o banco quiser o estorno forçado, ele deve ser um escopo à parte (algo como `ledger.admin`), com trilha própria, e não um parâmetro do estorno comum. Isso cria saldo negativo além do limite de cheque especial e exige uma regra nova no `UPDATE` condicional e um evento de auditoria específico.

### Como atribuir um estorno à pessoa que o pediu

O ledger só enxerga o `client_id` do token, que é o do back-office, e não o operador. O back-office deve guardar o `X-Correlation-Id` junto ao seu registro de quem agiu, e uma versão posterior pode aceitar um campo opcional de ator, gravado na trilha. Se o auditor exigir atribuição individual desde o primeiro dia, a mudança é pequena, mas toca o contrato do estorno e o catálogo da trilha.

### A criação de conta deve aceitar o identificador do sistema de origem

Como a conta nasce em outro sistema, um identificador externo único tornaria a criação idempotente por natureza. Não agora: a rota é de apoio, e a decisão pertence ao desenho da integração com o sistema de contas, que ainda não existe. Nessa hora o caminho natural é um `externalId` único. Até lá, a `Idempotency-Key` opcional da criação ([documento de arquitetura 0035](documento-arquitetura/0035-idempotency-key-opcional-na-criacao-de-conta.md)) cobre a repetição do mesmo pedido pelo mesmo chamador, mas não reconhece a mesma conta pedida por dois chamadores. A resposta contrária muda o contrato da criação e acrescenta uma coluna única em `accounts`.

### O ledger deve recusar lançamentos em conta bloqueada ou encerrada

Na primeira versão o controle fica no sistema de origem, que deixa de chamar o ledger. Situação da conta e saldo bloqueado devem entrar juntos, quando entrarem. Na resposta contrária o ledger passa a conhecer a situação da conta, com um predicado a mais no `UPDATE` condicional e um código de erro novo, e o saldo bloqueado muda também a consulta de saldo.

### Como entra o histórico do ledger anterior

Carregar o saldo de abertura de cada conta como um crédito com `occurred_at` da data da migração e manter o histórico no ledger anterior pelo prazo de retenção. O novo ledger não responderia saldo de instantes anteriores à migração. Se o banco precisar do histórico no ledger, cada lançamento histórico entra com `recorded_at` do momento da carga, e a consulta por instante não reproduziria as datas antigas ([documento de arquitetura 0013](documento-arquitetura/0013-tempo-do-saldo-registrado-em-vs-ocorrido-em.md)).

### O negócio vai precisar de saldo por data de competência

Tratar o assunto como contabilidade, no razão geral, e não no ledger de contas. Se o requisito vier mesmo assim, o caminho é uma projeção bitemporal alimentada pelos eventos do outbox, em tabela própria, sem tocar `ledger_entries` (EV-17 em [Evolução futura](../11-evolucao/evolucao-futura.md)).

### Qual é o atraso aceitável para a publicação de eventos

Cinco segundos no p99 em operação normal (NFR-13), a confirmar com a conciliação, que é a principal consumidora. Se ela exigir menos, o ajuste é de configuração do Worker (`Outbox:IdlePollMs` e número de instâncias). Se exigir tempo quase real, a captura por log (CDC) entra em pauta.

### O que acontece com os dados depois dos dez anos

É assunto para um projeto próprio, iniciado bem antes do nono ano, com o jurídico e o encarregado de dados: arquivamento frio ou anonimização do cadastro. A anonimização zera `holder_document_encrypted`, `holder_document_blind_index` e `holder_document_key_version`. A exclusão física dos lançamentos, se o jurídico a exigir, depende do particionamento por tempo da tabela.

## Segurança e privacidade

### Como atender pedidos de titulares

O pedido chega pelo canal de atendimento do banco, e o ledger precisa localizar as contas de um documento, o que é a busca pelo índice cego. A API não tem rota para isso. Recomendo um comando administrativo no Worker, que hoje não existe (os comandos atuais são `--migrate` e `--inspect-account`), que leia o documento da entrada padrão, calcule o índice em memória e grave um evento na trilha. Assim a chave do índice nunca sai do processo e a execução fica registrada. Um script SQL manual não serve, porque não calcula o HMAC sem expor a chave a quem opera. Enquanto o comando não existir, o atendimento ao titular fica sem procedimento.

### Vale restringir cada cliente às operações que ele usa

Na primeira versão qualquer chamador autenticado com o escopo certo opera qualquer conta. Não restringir agora: são poucos chamadores conhecidos, e a tabela de regras seria mais uma coisa para manter. Gravar o `client_id` em tudo deixa a porta aberta, e com o terceiro chamador a restrição deixa de ser luxo (EV-19). Restringir já pede uma lista de operações permitidas por `client_id`, em configuração ou no emissor.

### A base legal e o prazo de dez anos se aplicam ao documento do titular

Falta saber se a base legal e o prazo de guarda valem para o documento em si ou só para os lançamentos. Até o jurídico responder, manter o documento durante a retenção, porque sem ele a conta fica sem vínculo e não dá para atender uma ordem judicial sobre um titular específico. Se o jurídico disser que o documento pode sair antes, a anonimização da conta se antecipa e o procedimento de [Proteção de dados](../07-consistencia-e-seguranca/protecao-de-dados.md) muda.

### A agregação da auditoria de leitura basta para a conformidade

A auditoria de leitura registra um evento por consulta. Se o volume pesar, a saída é agregar o saldo atual por cliente, conta e minuto e manter completos o extrato e o saldo histórico, que são as consultas que um auditor de fato pede. A conformidade precisa dizer se a agregação basta. O volume dessa auditoria na escala das premissas ainda não foi medido, e a resposta contrária move a auditoria de leitura para um armazenamento próprio.

## Arquitetura e operação

### Uma conta com divergência de integridade deve parar de receber lançamentos

Hoje a conferência detecta e avisa, e a conta continua operando, porque o Worker nunca corrige sozinho. Não bloquear na primeira versão: bloquear a conta de um cliente é decisão de negócio, e o alerta chama alguém que decide. Se a resposta for parar, o mecanismo é barato: uma coluna booleana em `account_balances`, um predicado a mais no `UPDATE` condicional e um código de erro novo, como `ACCOUNT_QUARANTINED`, 409. Um ponto relacionado: quando o saldo derivado está errado e os lançamentos estão certos, não há fato novo a lançar, e a correção é refazer a linha de `account_balances` a partir do ledger num procedimento auditado que ainda não existe como comando.

### É preciso garantir a ordem dos eventos por conta

A primeira versão não garante ordem, e o contrato manda o consumidor usar `accountVersion`. Manter assim e tratar o primeiro consumidor que precise de ordem estrita como motivo para reavaliar, possivelmente com uma partição por conta no broker. Um publicador único não basta, porque uma falha de publicação reordena, e garantir a ordem exigiria parar a linha por conta, o que contraria o desacoplamento do outbox.

### Como o particionamento mensal preserva a unicidade

Em tabela particionada, toda restrição de unicidade precisa incluir a chave de partição, e particionar por `recorded_at` afeta o identificador do lançamento, `(account_id, account_version)` e o estorno único. O previsto é incluir `recorded_at` na unicidade da versão (mais fraca sozinha, mas sustentada pela linha travada e pela conferência) e mover o estorno único para uma tabela pequena e não particionada, inserida na mesma transação, como descreve o EV-04 de [Evolução futura](../11-evolucao/evolucao-futura.md). A alternativa é particionar por hash de `account_id`, que preserva as três garantias ao preço de perder a poda e o armazenamento em camadas por idade. A decisão fica para quando o gatilho de 300 GB se aproximar. O volume real de escrita, que dirá quando isso acontece, ainda não foi medido.

### Uma mensagem venenosa do outbox deve ser apagada depois de N tentativas

Hoje a mensagem que o broker nunca aceita é reivindicada de novo a cada 30 segundos, nunca apagada, com alerta na quinta tentativa ([documento de arquitetura 0029](documento-arquitetura/0029-publicador-do-outbox-com-circuito-por-mensagem.md)). Manter, porque apagar perde dinheiro sem ninguém ver. A resposta contrária exige uma regra de negócio que diga o que fazer com o evento descartado e um lugar onde ele fique registrado.

### O ambiente local precisa mostrar o failover

O repositório sobe um PostgreSQL só. Não demonstrar o failover no ambiente local: ele só se exerce numa topologia com standby, fora do repositório ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)). Demonstrá-lo localmente custa dois standbys e um orquestrador de promoção no Compose, cerca de dois dias de trabalho, e a topologia local deixaria de ser a do teste de integração.

## Leia também

- [Regras de negócio](../02-contexto-e-requisitos/regras-de-negocio.md): as regras BR-nn citadas nas recomendações.
- [Evolução futura](../11-evolucao/evolucao-futura.md): os itens com gatilho que respondem a algumas perguntas.
- [Segurança](../07-consistencia-e-seguranca/seguranca.md): o modelo de ameaças por trás das perguntas de segurança.
