# Contexto de negócio

O problema que o ledger resolve, o que o desenho persegue, o que ficou dentro e fora da primeira versão e quem usa o sistema. As regras de negócio estão em [Regras de negócio](regras-de-negocio.md), o comportamento visível a quem chama em [Requisitos funcionais](requisitos-funcionais.md) e as metas de desempenho e disponibilidade em [Requisitos não funcionais](requisitos-nao-funcionais.md).

## Por que o ledger existe

Em um banco digital, quase tudo o que o cliente faz termina em duas perguntas: o que aconteceu na conta e quanto há nela. Um Pix, uma compra no cartão, uma tarifa e um estorno viram, cada um, um crédito ou um débito, e o ledger é onde isso fica registrado de forma definitiva. Para o cliente e para o regulador, o saldo que ele devolve é a verdade.

O ledger anterior é lento, instável nos picos e difícil de manter. Ninguém mediu esses sintomas e não há linha de base, mas dá para dizer o que cada um custa. A lentidão se encadeia: o canal que espera demais desiste, e desistir não quer dizer que a operação não aconteceu, porque o débito pode ter sido gravado enquanto a resposta se perdia. O canal reenvia sem saber, e sem defesa explícita o cliente paga duas vezes por um problema de desempenho. A instabilidade pega o pior momento, porque dia de pagamento, véspera de feriado e Black Friday concentram a movimentação, e a falha pela metade é pior que a queda total, que todo mundo vê: se o sistema grava o débito e perde o aviso para a conciliação, o banco passa a ter duas versões do mesmo fato. A manutenção difícil esconde as outras duas, porque com a regra espalhada a equipe evita mexer, e a primeira pergunta de uma auditoria ("por que esta conta tinha este saldo naquela data?") exige arqueologia.

O projeto trata os três sintomas como um problema só, de confiança: rápido o bastante para não induzir reenvio, estável o bastante para nunca falhar pela metade e simples o bastante para ser alterado sem medo. Boa parte dos [documentos de arquitetura](../03-principios-e-decisoes/documento-arquitetura/README.md) deriva dessa leitura.

## Objetivos

Os números de volume, latência e disponibilidade são metas assumidas para orientar o desenho, e nenhum veio de medição do banco.

| Objetivo | Meta | Requisito |
|---|---|---|
| Não perder dinheiro | Nenhum lançamento confirmado se perde (RPO zero) | NFR-06 |
| Não duplicar dinheiro | Nenhum lançamento duplicado por reenvio com a mesma chave | FR-07, FR-08 |
| Saldo sempre coerente | Saldo igual à soma dos lançamentos e nunca abaixo de `-overdraft_limit` | FR-09, FR-20 |
| Aguentar o pico | Vazão de escrita e de leitura de saldo, inclusive na conta mais movimentada | NFR-01, NFR-02 |
| Responder rápido | p99 de escrita e de consulta de saldo | NFR-03, NFR-04 |
| Ficar de pé | Disponibilidade e tempo de recuperação | NFR-05, NFR-07 |
| Responder à auditoria | Saldo em qualquer instante por dez anos, sem remoção física de lançamento | NFR-08, FR-15 |
| Poder mudar sem medo | Regra de negócio em um lugar só e todo requisito funcional com teste automatizado | NFR-15 |

## Escopo

Está dentro da primeira versão o cadastro mínimo de conta (documento do titular, moeda e limite de cheque especial, que vale zero por padrão), os créditos e débitos sempre com chave de idempotência, o estorno, o saldo atual e em um instante do passado, o extrato paginado, um evento por lançamento confirmado, a autenticação por escopo, a cifragem do documento do titular, a trilha de auditoria, a observabilidade e a conferência periódica entre saldos e lançamentos.

Ficaram de fora conversão entre moedas, saldo bloqueado ou reservado, limites e tarifas, particionamento físico da tabela de lançamentos, réplica de leitura, cache distribuído, captura de mudanças, operação em várias regiões, event sourcing completo e interface web. O gatilho que justificaria cada item está em [Evolução futura](../11-evolucao/evolucao-futura.md).

Há ainda ausências que quem integra costuma esperar:

- **Transferência atômica entre duas contas.** A API só conhece lançamentos de uma conta. Um Pix interno é, para o ledger, um débito em uma conta e um crédito em outra, cada um com a sua chave. Quem orquestra os dois e compensa a falha de um deles é o sistema chamador.
- **Contabilidade geral e decisão sobre a operação.** Plano de contas, partidas dobradas, cadastro completo do cliente, prevenção a fraude, autorização e notificação ficam em outros sistemas. O ledger registra o que lhe pedem.
- **Juros e encargos do cheque especial.** O limite existe e é respeitado, mas o cálculo do que ele custa não.
- **Histórico do sistema anterior e relatórios regulatórios.** A migração do histórico é uma pergunta em aberto ([Questões em aberto](../03-principios-e-decisoes/questoes-em-aberto.md)), e os relatórios são de quem consome os dados.
- **Pré-autorização, agendamento e lote.** Sem saldo reservado, a pré-autorização de cartão vira um débito na autorização e um estorno no cancelamento, dois lançamentos onde um bastaria. Também não há lançamento agendado, lote em uma chamada, estorno parcial, estorno que force a conta abaixo do limite nem consulta de um lançamento isolado (o extrato por período cobre o caso).

## Quem usa e o que espera

Os consumidores são sistemas, mas por trás de cada um há uma pessoa com uma necessidade diferente.

| Parte | Como usa o ledger e o que espera |
|---|---|
| Backend do aplicativo | Maior consumidor em volume, quase só lê saldo e extrato. Quer resposta rápida e previsível, com o escopo `ledger.read` |
| Pix e cartões | Principais escritores. Chegam com identificador próprio (o de ponta a ponta do Pix, o código de autorização do cartão), sofrem timeout de rede e repetem chamadas por reflexo. Querem a decisão final, débito aceito ou recusado por falta de saldo, sem consulta prévia. Têm `ledger.read` e `ledger.write` |
| Conciliação e financeiro | Lê extratos de períodos inteiros, página por página, confere o encadeamento dos saldos e consome os eventos. Quando acha diferença, precisa corrigir sem editar o passado |
| Atendimento ao cliente | Usa o back-office, que chama o ledger. Precisa dizer quanto havia na conta em um instante de ontem, o que aconteceu depois e disparar o estorno de uma cobrança indevida, sem escalar para engenharia |
| Compliance, risco e auditoria | Só leem. Querem trilha de auditoria, retenção de dez anos e saldo reconstituível em qualquer instante, com resposta repetível: o mesmo saldo no mesmo instante dá o mesmo número |
| Segurança e encarregado de dados | Dado pessoal mínimo, cifrado e fora dos logs, acesso por escopo e resposta rápida a incidente |
| Plantão e engenharia | Verificações de saúde que reflitam o estado real e alertas que apontem a causa, para saber em minutos se há dinheiro em risco ou só lentidão ([Saúde e observabilidade](../08-resiliencia-e-operacao/saude-e-observabilidade.md)). Código legível e testes que protegem a mudança |
| Diretoria e clientes finais | Fim dos incidentes de pico e custo de mudança previsível. O cliente final não chama a API, mas sente cada erro: quer saldo certo e cobrança uma vez só |

## Jornadas

### Débito de Pix com repetição

O serviço de Pix recebe uma ordem de R$ 80,00 contra uma conta com R$ 1.000,00 e chama `POST /v1/accounts/{accountId}/entries` com `type` igual a `DEBIT`, usando o identificador de ponta a ponta como `Idempotency-Key`. A resposta se perde na rede, e o Pix repete a chamada com a mesma chave e o mesmo corpo. Recebe a resposta original, com `balanceAfter` "920.00" e `Idempotent-Replayed: true`. Se a conta tivesse R$ 50,00, a resposta seria 422 `INSUFFICIENT_FUNDS`, e o Pix recusaria a ordem sem consultar o saldo antes. Consultar antes não ajudaria, porque entre a consulta e o débito outra chamada pode ter gasto o dinheiro: o débito é a verificação.

### Contestação no suporte

O cliente diz que ontem às 23h40 tinha R$ 2.300,00 e que o aplicativo mostrou menos. O back-office chama `GET /v1/accounts/{accountId}/balance?asOf=...` com o horário de Brasília (`-03:00`) e depois pede o extrato do período. Como cada item traz `balanceAfter`, a história se lê de uma vez: o saldo era mesmo R$ 2.300,00 e, doze minutos depois, entrou um débito de R$ 400,00 que o cliente não reconhece. O back-office pede o estorno com uma `Idempotency-Key` própria, e o novo lançamento aparece no extrato apontando para o original, que continua intacto.

### Conciliação diária

Ao fim do dia, a conciliação lê o extrato de cada conta para o dia em Brasília (meia-noite a meia-noite, com `-03:00`), em páginas de até 200 itens, e compara com o arquivo de liquidação. Antes de comparar valores, confere o encadeamento: cada `balanceAfter` deve ser o anterior mais ou menos o valor do lançamento. Se fecha e ainda há diferença contra a câmara, o problema está do outro lado ou falta um lançamento, e a correção entra como lançamento novo ou estorno. Se não fecha, é incidente do ledger, e a conferência de integridade já deveria ter avisado o plantão.

### Estorno de cobrança duplicada

O sistema de cartões percebe que uma compra de R$ 250,00 foi lançada duas vezes, com chaves diferentes por falha da própria integração, e pede o estorno do segundo lançamento. O ledger cria um crédito de R$ 250,00 com `reversesEntryId` apontando para ele. Estornar o mesmo lançamento de novo, com outra chave, dá 409 `ENTRY_ALREADY_REVERSED`, e repetir a chamada com a mesma chave devolve a resposta original.

### Auditoria de fechamento

O auditor quer provar o saldo de 31 de dezembro para uma amostra de contas. Pede `asOf` no último instante do ano, compara com o balancete e puxa o extrato de dezembro para ver o encadeamento. Depois consulta na trilha de auditoria o histórico das conferências de integridade do período, que deve ser uma série sem divergências.

## O que custa uma inconsistência

O débito duplicado tira dinheiro do cliente, e o banco devolve, paga atendimento e, conforme o caso, indenização. O crédito duplicado custa mais ao banco, porque o cliente pode gastar o excedente antes da correção e parte do valor pode não voltar. Débito perdido é prejuízo direto do mesmo tamanho. Saldo estourado por corrida é crédito concedido sem análise, e saldo errado por alguns segundos pode levar um canal a aprovar compra sem fundos. Soma-se o custo que ninguém registra, as horas de conciliação, suporte e engenharia para investigar uma diferença de centavos.

Com valores hipotéticos: num pico de uma hora a 2.000 lançamentos por segundo (7,2 milhões), uma falha de reenvio que duplicasse um em cada 100 mil geraria 72 duplicidades. A R$ 200 de valor médio são R$ 14.400 movimentados indevidamente e um caso novo a tratar a cada 50 segundos. A taxa de duplicidade tem de ser zero por construção.

## Como o sucesso se mede

Os testes de cada requisito funcional estão em [Rastreabilidade de requisitos](rastreabilidade.md), e as metas de desempenho e disponibilidade são premissas ainda não medidas em escala ([Limites conhecidos](../09-qualidade/limites-conhecidos.md)). Três sinais de produto não têm alvo de desempenho e servem para observar o uso. O primeiro é a proporção de respostas com `Idempotent-Replayed` sobre o total de escritas, em que um pico indica rede instável nos consumidores. O segundo é a proporção de estornos sobre lançamentos, a investigar acima de 0,5%, valor que só dados reais calibram. O terceiro é o tempo para um novo consumidor fazer a primeira escrita em ambiente de teste, até um dia útil, hipótese que depende de contrato claro e de exemplos.

## Leia também

- [Restrições e premissas](restricoes-e-premissas.md): o que limita o desenho e o que foi suposto para dimensioná-lo.
- [Riscos e alternativas rejeitadas](../03-principios-e-decisoes/riscos-e-trade-offs.md): os riscos e as respostas do projeto.
- [Princípios de arquitetura](../03-principios-e-decisoes/principios-de-arquitetura.md): o que orienta toda decisão de desenho.
