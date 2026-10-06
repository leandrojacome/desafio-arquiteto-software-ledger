# Evolução futura

A primeira versão entrega o núcleo (gravar certo, não duplicar, consultar o passado e recusar o que não cabe) e deixa de fora o que [Contexto de negócio](../02-contexto-e-requisitos/contexto-de-negocio.md) registra como fora de escopo. Cada item abaixo tem um gatilho: um número que o próprio sistema consegue medir ou um fato verificável, como uma data, um produto novo ou uma exigência escrita. Os números de volume vêm da conta de [Capacidade e escala](../08-resiliencia-e-operacao/capacidade-e-escala.md), que é premissa e não dado do banco.

A ordem das seções começa pelo que mede ou protege o que a primeira versão já promete (EV-01 a EV-06), porque ampliar um sistema cujas premissas não foram medidas não adianta. Seguem a dor técnica previsível, o que depende de o produto pedir, o que o risco e a escala exigem e, por último, a qualidade dos próprios testes.

| ID | Item | Gatilho principal | Esforço | Depende de |
|---|---|---|---|---|
| EV-01 | Provas de carga e de falha em hardware de produção | Antes de qualquer tráfego real, e a cada trimestre | M | |
| EV-02 | Cronometragem dos comandos da escrita e do commit | Antes das provas do EV-01, ou o primeiro alerta de latência de escrita sem causa visível | S | |
| EV-03 | Alertas e painéis versionados e testados | O primeiro ambiente com pilha de monitoração | S | |
| EV-04 | Particionamento mensal de `ledger_entries` | 300 GB ou 1 bilhão de linhas (cerca de 40 dias, no volume premissado) | L | EV-01 |
| EV-05 | Contas muito quentes | 80 lançamentos por segundo sustentados numa conta | M | EV-01 |
| EV-06 | Integridade reforçada e quarentena de conta | A primeira divergência em produção, ou exigência de auditoria | L | EV-04 |
| EV-07 | Réplica de leitura | Primário acima de 60% de CPU por causa de leitura | M | EV-04 |
| EV-08 | CDC com Debezium | Atraso de publicação abaixo de 1 s exigido por um consumidor | L | EV-04 |
| EV-09 | Cache de saldo | p99 do saldo acima de 50 ms apesar do restante | M | EV-07 |
| EV-10 | Snapshots de fechamento | Fechamento diário em lote acima de 30 min | M | EV-04 |
| EV-11 | Cota distribuída por chamador | Segundo chamador crítico, ou cota contratual | S | |
| EV-12 | Validação da hierarquia de timeouts na subida | A primeira mudança de timeout em ambiente parecido com produção | S | |
| EV-13 | Medição do atraso de publicação por evento | Um consumidor com atraso contratual, ou a necessidade de medir o p99 de 5 s sob carga | S | |
| EV-14 | Saldo bloqueado e situação da conta | O primeiro produto que precisa reservar valor | L | |
| EV-15 | Transferência atômica entre contas | Meia-transferência órfã em volume relevante | M | EV-05 |
| EV-16 | Multi-moeda | O primeiro produto fora do real | L | EV-15 |
| EV-17 | Saldo por data de negócio | Exigência contábil de saldo retroativo | L | EV-10 |
| EV-18 | Lacunas de contrato | O primeiro pedido concreto de um chamador | S | |
| EV-19 | Segurança evolutiva | Terceiro chamador, consumidor externo ou pedido jurídico | M | |
| EV-20 | Multi-região | Política de continuidade que exija sobreviver à perda de uma região | L | EV-04, EV-07 |
| EV-21 | Divisão por conta em vários bancos | Escrita sustentada acima de metade do que o maior primário aguenta | L | EV-04, EV-05, EV-07 |
| EV-22 | Migração para a LTS seguinte do .NET | O suporte do .NET 10 termina em 14 de novembro de 2028 | S | |
| EV-23 | Teste de mutação e de contrato por consumidor | O primeiro consumidor real, ou um defeito que a cobertura não pegou | S | |

Esforço é a estimativa de uma pessoa: S até uma semana, M de duas a quatro semanas, L mais do que isso.

## Antes de crescer

### EV-01. Provas de carga e de falha em hardware de produção

É a dívida da primeira versão, não uma evolução. Cada número de [Capacidade e escala](../08-resiliencia-e-operacao/capacidade-e-escala.md) é premissa, e a máquina de desenvolvimento não demonstra capacidade nem failover (as hipóteses estão em [Limites conhecidos](../09-qualidade/limites-conhecidos.md)). O exercício monta a [topologia de produção](../04-modelos-c4/implantacao-producao.md) e roda, em ordem: o cenário `mixed` do k6 no perfil inteiro por uma hora e depois por várias; uma rampa de conta quente de 25 a 200 lançamentos por segundo, para achar o joelho da curva que a conta estima entre 120 e 165; um `spike` de três vezes o perfil por 5 minutos, sem resposta `500`; o failover com carga, conferindo que nenhum `201` se perdeu e medindo o tempo até a primeira escrita; a restauração de um backup completo e a pontual no tempo, cronometradas; um standby com 20 e 50 ms de latência; o disco de dados enchendo; o relógio do standby atrasado antes da promoção; a fonte de chaves fora durante uma implantação; e um token do emissor real do banco.

Começa com exercícios manuais de roteiro escrito e passa a automático em homologação, com k6, `tc netem` ou Toxiproxy. É condição antes do primeiro tráfego real, e depois se repete a cada trimestre e a cada mudança de topologia ou de versão maior do PostgreSQL. O resultado corrige premissas e requisitos, e os cenários 2, 12, 13 e 14 de [Cenários de falha](../08-resiliencia-e-operacao/cenarios-de-falha.md) só serão exercitados de verdade nele.

### EV-02. Cronometragem dos comandos da escrita e do commit

O instrumento `ledger_db_command_duration_seconds` aceita `insert_entry`, `update_balance`, `insert_outbox` e `insert_idempotency_key` em `operation`, mas a escrita não os emite, e o commit também não é cronometrado. Diante de uma latência alta, nada diz se o tempo está no lock da linha, nos inserts ou no commit, e é essa atribuição que a calibração dos limites de escrita e do pico pede. A mudança é cronometrar cada comando da unidade de trabalho e o commit, com o rótulo novo no conjunto fechado e os testes de catálogo e de cardinalidade atualizados.

### EV-03. Alertas e painéis versionados e testados

As regras de alerta e os painéis de [Indicadores, objetivos e alertas](../08-resiliencia-e-operacao/slos-e-alertas.md) e de [Saúde e observabilidade](../08-resiliencia-e-operacao/saude-e-observabilidade.md) são especificação em texto, e nenhuma expressão rodou contra o tráfego do ledger. A mudança é versioná-las como arquivos, validar a sintaxe com `promtool check rules`, cobrir os alertas de maior consequência com `promtool test rules` e conferir em teste que toda métrica citada existe no catálogo.

### EV-04. Particionamento mensal de `ledger_entries`

No volume premissado a tabela passa de 300 GB em cerca de 40 dias, de um bilhão de linhas em 58 dias e chega a 27 TB em dez anos, então o particionamento é pré-requisito de um lançamento em escala se o banco confirmar o volume. Também dispara autovacuum levando mais de uma hora, `CREATE INDEX CONCURRENTLY` levando mais de quatro, ou p99 do saldo em um instante acima de 30 ms em contas antigas.

A tabela passa a ser particionada por faixa de `recorded_at`, uma partição por mês, criadas com três meses de antecedência, e o saldo em um instante continua sendo uma busca por índice que para na primeira partição com resultado. O trabalho está na unicidade, que numa tabela particionada precisa incluir a chave de partição. A chave primária vira composta, e o índice único parcial de `reverses_entry_id`, que impede dois estornos do mesmo lançamento ([documento de arquitetura 0002](../03-principios-e-decisoes/documento-arquitetura/0002-ledger-imutavel-somente-insercao.md)), deixa de ser global: a saída é uma tabela pequena e não particionada com o par lançamento original e estorno, inserida na mesma transação. Como as migrações só andam para a frente, a mudança se faz em duas versões. As partições antigas deixam de receber escrita e podem ir para armazenamento mais barato, o que respeita a retenção de dez anos, e `idempotency_keys` e `outbox_messages` ganham se forem partidas por dia, porque a poda vira descartar uma partição. A migração se valida com cerca de 100 milhões de linhas sintéticas, conferindo com `EXPLAIN` que o plano do saldo segue sendo busca ordenada com parada no primeiro resultado.

### EV-05. Contas muito quentes

A linha de `account_balances` tem teto de cerca de 166 lançamentos por segundo, e o p99 de escrita cruza a meta antes disso. O primeiro sistema que lançar a contrapartida de todo Pix numa conta única da instituição chega ao teto muito antes dos 2.000 por segundo totais. Dispara uma conta com 80 lançamentos por segundo por um minuto, espera de lock com p99 de 30 ms ou mais ou mais de 0,1% das escritas de uma conta em `503` por `lock_timeout`.

O primeiro remédio não custa código: combinar com os chamadores que contas internas quentes sejam espalhadas em várias (`liquidacao_01` a `liquidacao_16`) e rotear no balanceador por hash do `accountId`, o que faz o limitador por conta valer de verdade. Se não bastar, vem o microlote: a API junta, em poucos milissegundos, as requisições concorrentes da mesma conta e as aplica numa transação só, com um `UPDATE` final do saldo. O teto da conta vai a milhares por segundo ao custo de 1 a 2 ms de latência, e como muda a forma de decidir o saldo do [documento de arquitetura 0004](../03-principios-e-decisoes/documento-arquitetura/0004-saldo-corrente-com-update-condicional.md), é uma decisão arquitetural nova. Dividir o saldo em baldes por conta foi descartado, porque quebra a ordem total que o `balance_after` e o `asOf` exigem.

### EV-06. Integridade reforçada e quarentena de conta

A conferência de integridade detecta, alerta e nunca corrige, e tem dois buracos (cenário 15 de [Cenários de falha](../08-resiliencia-e-operacao/cenarios-de-falha-9-a-16.md)): não relê o histórico antigo inteiro, e uma conta com divergência continua recebendo lançamentos. O gatilho é a primeira divergência em produção, uma auditoria que exija evidência criptográfica de imutabilidade ou seis meses de operação, o que vier primeiro.

Cada partição fechada ganha um selo, um resumo criptográfico das linhas em ordem com a contagem, guardado numa tabela própria e replicado para um armazenamento de retenção travada, fora do alcance de quem opera o banco. Reler uma partição de 226 GB leva, por estimativa, de 15 a 30 minutos, então a conferência revalida algumas por semana e o histórico inteiro em um trimestre. Se a auditoria quiser mais, o complemento é uma cadeia de hashes por conta. A quarentena é a segunda parte: a conferência marca a conta, e a escrita recusa com um código de erro próprio até alguém liberar, o que exige o estado de conta do EV-14. Só liga depois de um histórico limpo da conferência em produção, porque um falso positivo travaria a conta de um cliente.

## Quando a dor aparecer

### EV-07. Réplica de leitura

Dispara a CPU do primário em 60% ou mais no p95 por uma semana, com leitura respondendo por mais da metade do tempo total em `pg_stat_statements`, o p99 do extrato acima de 200 ms por contenção no pool de leitura ou consultas pesadas da conciliação empurrando o p99 de escrita acima de 150 ms. A mudança é uma ou duas réplicas assíncronas, separadas dos standbys síncronos, e uma segunda fonte de dados do Npgsql, o que se encaixa na separação de pools de [Políticas de resiliência](../08-resiliencia-e-operacao/politicas-de-resiliencia.md). O saldo atual sai sempre do primário, porque é com ele que se decide um débito. O `asOf` sai da réplica só para instantes mais velhos que o maior tempo de transação mais o atraso tolerado, e o extrato volta ao primário se o atraso passar de dois segundos. Consultas longas na réplica são canceladas por conflito com a recuperação, e a configuração que as protege (`max_standby_streaming_delay`, `hot_standby_feedback`) incha o primário. O extrato também pode ficar até dois segundos atrás do saldo atual, e o contrato precisa dizer isso.

### EV-08. CDC com Debezium

O polling do outbox é simples e funciona. Ler o WAL daria latência de milissegundos e tiraria do primário a consulta a cada 200 ms. Dispara um consumidor que exija atraso abaixo de 1 segundo, como o antifraude em tempo real, o polling custando 5% ou mais da CPU do primário ou muitos consumidores independentes precisando reprocessar histórico. O Debezium vive sobre o Kafka Connect, então a decisão anda junto com a troca do broker e substitui o [documento de arquitetura 0008](../03-principios-e-decisoes/documento-arquitetura/0008-rabbitmq-entrega-ao-menos-uma-vez.md). O slot lógico faz o WAL se acumular até encher o disco se o leitor parar (o cenário 13), o que torna obrigatório um teto de retenção por slot, e no PostgreSQL 16 os slots não acompanham o failover para o standby. O contrato com os consumidores (`message_id` estável, entrega pelo menos uma vez) não muda.

### EV-09. Cache de saldo

O [documento de arquitetura 0009](../03-principios-e-decisoes/documento-arquitetura/0009-sem-cache-na-v1.md) fixou a regra de não ter cache até medir. Dispara se, com 10.000 leituras por segundo misturadas às 2.000 escritas, o p99 do saldo passar de 50 ms de forma sustentada depois do ajuste do banco e da réplica, ou se a leitura ameaçar o p99 de escrita. O EV-07 vem antes porque a réplica resolve capacidade com menos risco, mas não a latência de busca por chave, que é o que o cache atacaria. A mudança é um Redis em cache-aside só para o saldo atual, com a versão da conta no valor, TTL de poucos segundos e uso proibido na decisão de débito, que continua sendo o `UPDATE` condicional. O contrato passa a dizer que o valor pode estar defasado até o TTL. Os riscos são a invalidação perdida, a avalanche quando uma chave quente expira e o Redis fora do ar, que precisa cair de volta para o banco sem multiplicar a carga.

### EV-10. Snapshots de fechamento

O `balance_after` resolve o saldo de uma conta em qualquer instante, mas não o de todas as contas em um instante, que é o que um fechamento contábil pede: 20 milhões de buscas por índice. O [documento de arquitetura 0003](../03-principios-e-decisoes/documento-arquitetura/0003-saldo-apos-em-cada-lancamento.md) deixou os snapshots como plano B. Dispara o fechamento diário em lote passando de 30 minutos, a revalidação de partições (EV-06) passando de 6 horas ou o p99 do saldo em um instante seguir acima de 50 ms depois do particionamento. A mudança é uma tabela de snapshots por conta e por dia, gerada pelo Worker, nunca fonte de verdade e sempre reconstruível, que serve também à conferência e ao EV-17.

### EV-11. Cota distribuída por chamador

Os limites de taxa vivem na memória de cada instância, então com N instâncias o teto efetivo é N vezes o configurado, de propósito ([documento de arquitetura 0011](../03-principios-e-decisoes/documento-arquitetura/0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md)). Dispara um segundo chamador crítico que precise de garantia de cota, um incidente causado por essa multiplicação ou a cota virar cláusula contratual. A cota por chamador sobe para o gateway de entrada, que evita uma dependência nova no caminho de toda requisição, ou para contadores no Redis. O limite por conta continua local, com roteamento por hash do `accountId` (EV-05).

### EV-12. Validação da hierarquia de timeouts na subida

Os valores padrão respeitam a ordem espera pelo lock, comando, `statement_timeout`, requisição inteira, mas nenhum validador a confere: a subida valida cada valor contra a sua faixa, e uma configuração que inverta a ordem sobe sem aviso ([Políticas de resiliência](../08-resiliencia-e-operacao/politicas-de-resiliencia.md)). A mudança é um validador que recuse a subida nomeando as duas chaves em conflito, com teste de cada inversão.

### EV-13. Medição do atraso de publicação por evento

O ledger mede a idade da mensagem pendente mais antiga, mas não o atraso de cada evento entre o commit e a publicação, então a meta de p99 de 5 segundos (NFR-13) só é verificável no ponta a ponta, em escala local. A mudança é um histograma do intervalo entre `created_at` e a confirmação do broker, emitido ao marcar as mensagens publicadas, com um limite de bucket em 5 s.

## Quando o produto pedir

### EV-14. Saldo bloqueado e situação da conta

Sem saldo reservado, a pré-autorização de cartão debita na autorização e estorna no cancelamento, o que funciona mas gera dois lançamentos onde um bastaria, e o bloqueio judicial não tem como ser modelado. Dispara o primeiro produto que precise reservar valor antes de liquidar. A mudança é uma tabela de reservas e um valor reservado em `account_balances`: o `UPDATE` do débito passa a considerar o saldo menos o reservado, a consulta ganha um saldo disponível ao lado do contábil, e liberar ou efetivar uma reserva gera lançamento e eventos novos. Junto entra a situação da conta (ativa, bloqueada, encerrada, em quarentena), que o EV-06 reaproveita. A regra de saldo do documento de arquitetura 0004 muda, e isso é uma decisão arquitetural nova.

### EV-15. Transferência atômica entre contas

Na versão 1 uma transferência é um débito e um crédito independentes, e quem compensa a falha de um deles é o chamador. Dispara o volume de meias-transferências órfãs passar do que as compensações absorvem (mais de uma em cem mil, por exemplo), qualquer caso que exija intervenção manual toda semana ou o ledger virar dono de um produto de transferência. A mudança é uma rota com uma única `Idempotency-Key`, dois lançamentos ligados por um identificador de transferência e um só evento. É uma transação local, não uma saga, desde que as duas contas morem no mesmo banco, com uma regra contra deadlock: travar sempre em ordem crescente de `account_id`. A transferência amplifica o problema das contas quentes, porque cada uma toca duas linhas.

### EV-16. Multi-moeda

O campo de moeda existe desde a versão 1, com uma moeda por conta e sem conversão. Dispara o primeiro produto em moeda diferente do real, um projeto à parte e não uma extensão. A mudança traz precisão por moeda (duas casas no real, zero no iene, três no dinar kuwaitiano) e nenhuma conversão implícita: câmbio é um par explícito de lançamentos entre a conta do cliente e contas internas de câmbio, uma por moeda, com a taxa e a origem registradas. Precisa de transferência atômica, daí a dependência do EV-15, e relatórios e conferências passam a ser por moeda.

### EV-17. Saldo por data de negócio

A versão 1 não adotou o modelo bitemporal ([documento de arquitetura 0013](../03-principios-e-decisoes/documento-arquitetura/0013-tempo-do-saldo-registrado-em-vs-ocorrido-em.md)): um lançamento retroativo reescreveria a cadeia de saldos. Dispara uma exigência escrita da contabilidade ou do regulador de saber o saldo do dia D pelo que se sabe hoje, junto com a medida de que há lançamentos retroativos suficientes para importar (mais de 1% com `occurred_at` mais de um dia antes do `recorded_at`, por exemplo). O ledger não é tocado: uma projeção alimentada pelos eventos do outbox, numa tabela própria, soma por `occurred_at` a partir dos snapshots do EV-10. A recomendação do documento de arquitetura 0013 continua valendo: essa pergunta é de contabilidade, e a resposta mora no razão geral.

### EV-18. Lacunas de contrato

O que a API deixou de fora tem gatilhos próprios, quase sempre uma conversa com um chamador. A rota para ler um lançamento isolado (`GET /v1/accounts/{accountId}/entries/{entryId}`) entra no primeiro pedido, por ser mudança compatível. O lote entra se um chamador mostrar que a ida e volta por lançamento domina o custo dele, e precisa de uma chave de idempotência por item. O estorno parcial depende de decisão do produto ([Questões em aberto](../03-principios-e-decisoes/questoes-em-aberto.md)), e até lá a devolução parcial é um crédito novo. O estorno que força a conta abaixo do limite entra, se o banco quiser, como escopo à parte (`ledger.admin`) com trilha própria. O campo opcional de ator, para atribuir um estorno a quem o pediu no back-office, entra com a primeira exigência de auditoria individual, e o lançamento agendado fica num sistema acima do ledger. O esforço é pequeno por item, exceto o lote.

## Quando o risco ou a escala exigirem

### EV-19. Segurança evolutiva

[Segurança](../07-consistencia-e-seguranca/seguranca.md) nomeia o risco residual que a versão 1 aceita, e cada ponto tem o seu gatilho. A assinatura dos eventos do RabbitMQ entra com um consumidor fora da rede de confiança do banco ou depois do primeiro evento forjado. A cadeia de hashes entre lançamentos está no EV-06. Restringir cada chamador às operações que ele usa (a conciliação nunca debita, o app nunca estorna) entra com o terceiro chamador, e o passo seguinte é amarrar o token ao certificado do cliente com mTLS (RFC 8705), para um token roubado não funcionar em outra máquina. A chave de criptografia por conta, que faz destruir a chave equivaler a apagar o dado inclusive nos backups, entra quando o jurídico exigir que a eliminação alcance os backups. A rotação anual de chaves com recifragem em lote já existe, e falta ensaiá-la antes da primeira rotação de verdade.

### EV-20. Multi-região

Dispara a política de continuidade do banco exigir sobreviver à perda de uma região inteira, com RTO e RPO regionais explícitos e aceitos por escrito pelo negócio. A mudança é um standby assíncrono em outra região, com o WAL arquivado em armazenamento replicado, a fonte de chaves com réplica regional e um roteiro de troca de endereço, ensaiado como o failover do EV-01. O preço é abrir mão do RPO zero entre regiões: ele vale dentro da região, onde o commit é síncrono, e entre regiões a replicação é assíncrona, com perda possível de alguns segundos. Ativo-ativo com escrita nos dois lados foi descartado, porque duas regiões escrevendo na mesma linha de saldo é o conflito que o desenho todo evita. Se um dia houver escrita em duas regiões, a forma segura é cada conta ter uma região de origem, o que já é a divisão por conta do EV-21.

### EV-21. Divisão por conta em vários bancos

Se, depois de escalar o primário na vertical, particionar, tratar as contas quentes e ter réplica, a escrita ainda não couber em um primário, cada conta passa a morar em um cluster escolhido pela identidade dela, e a API roteia. Só é viável porque nenhuma operação da versão 1 toca duas contas ao mesmo tempo. Dispara a escrita sustentada acima de metade do que o maior primário disponível aguenta (medida no EV-01) ou um volume por nó que torne a restauração intolerável. A transferência entre bancos deixa de ser transação local, e é aí que uma saga com compensações passa a fazer sentido. São meses de esforço, e o único item que não deve começar sem uma medição que o justifique.

### EV-22. Migração para a LTS seguinte do .NET

O suporte do .NET 10 termina em 14 de novembro de 2028, pela política da Microsoft. A migração repete a lista do [documento de arquitetura 0021](../03-principios-e-decisoes/documento-arquitetura/0021-plataforma-dotnet-10.md): alvo de framework, imagens base, versões em `Directory.Packages.props` e os avisos novos dos analisadores, que aparecem todos de uma vez porque a compilação trata aviso como erro. O gatilho é o calendário: o trabalho começa com folga antes da data.

## Qualidade dos próprios testes

### EV-23. Teste de mutação e de contrato por consumidor

O [documento de arquitetura 0014](../03-principios-e-decisoes/documento-arquitetura/0014-estrategia-de-testes.md) deixou os dois de fora da versão 1. O teste de mutação (Stryker.NET sobre `Ledger.Domain`, sob demanda, com limiar de 80%) responde ao que a cobertura não responde: estes testes pegariam um defeito? Dispara o domínio ganhar regras novas, como saldo bloqueado ou transferência, ou um defeito escapar apesar de cobertura alta. Custa tempo de CI, então não roda por pull request. A comparação do documento OpenAPI gerado com o versionado já existe (`OpenApiContractTests`), e o que falta são os testes de contrato dirigidos pelo consumidor, que dependem do primeiro consumidor real ou da primeira proposta de mudança de contrato.

## O que fica fora da fila

Event sourcing completo continua sem motivo enquanto o domínio tiver duas operações e um estorno. Microsserviços custam mais consistência do que entregam, e o único corte que se pagou, o do Worker, já está feito. O Kafka só entra se o EV-08 entrar. O cache de saldo participando da decisão de débito não entra nunca, porque o `UPDATE` condicional existe para que o valor que decide seja o valor real.

## Leia também

- [Capacidade e escala](../08-resiliencia-e-operacao/capacidade-e-escala.md): a conta que sustenta os gatilhos de volume e de conta quente.
- [Limites conhecidos](../09-qualidade/limites-conhecidos.md): as hipóteses que o EV-01 transforma em medida.
- [Registro de decisões de arquitetura](../03-principios-e-decisoes/documento-arquitetura/README.md): as decisões que cada item reabre.
