# Documento de arquitetura 0029: Publicador do outbox com circuito por mensagem e sonda própria

## Contexto

O [documento de arquitetura 0011](0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md) pôs um circuit breaker em volta da publicação no Worker, com limiar de 50% de falhas numa janela de 30 segundos e no mínimo 10 operações. O desenho natural é o circuito em volta do lote inteiro, e ele falha num cenário concreto: com o broker bloqueado por alarme de memória a publicação não falha, pendura até o prazo de 5 segundos. Cabem seis lotes na janela de 30 segundos, e seis amostras nunca chegam às 10 do mínimo. O circuito ficaria fechado para sempre, e o Worker reivindicaria um lote novo a cada 5 segundos sem publicar nada, subindo `attempts` de mensagens saudáveis até o alerta de mensagem venenosa disparar à toa. O Polly ainda mantém o circuito `Open` depois do `BreakDuration` até alguém tentar executar.

## Decisão

O circuito conta mensagens, não lotes: cada publicação é uma amostra, e um lote de 200 que falha por inteiro abre o circuito na hora. Só contam como falha do broker a indisponibilidade, o prazo e a recusa. Um defeito do próprio ledger ao montar o evento não conta, nem a devolução por falta de fila ([documento de arquitetura 0033](0033-fila-de-retencao-e-publicacao-mandatory.md)).

O estado decide quanto reivindicar: com o circuito fechado e conectado, o lote cheio, e em qualquer outro estado, nada. Com o circuito aberto ou meio aberto, a cada volta o Worker tenta uma sonda: uma publicação vazia, transiente e com confirmação, na chave de roteamento `ledger.probe`, pelo mesmo pipeline do circuito, que só a deixa passar quando o tempo de espera acaba. Se a sonda passa, o circuito fecha e o lote seguinte sai inteiro. Os consumidores nunca a recebem, porque nenhuma fila é ligada a essa chave.

Sem conexão utilizável com o broker, o Worker também não reivindica. Essa situação não gera amostra para o circuito e tem sinal próprio, a readiness do Worker em `Degraded`. A reconexão é do ledger, com a recuperação automática da biblioteca desligada e espera `min(1 s · 2^n, 30 s)` com variação de até 20%, porque a recuperação da biblioteca tem intervalo fixo e o [documento de arquitetura 0011](0011-resiliencia-timeouts-retry-circuit-breaker-rate-limit.md) manda recuo exponencial.

O corpo publicado é o `payload::text` do `jsonb`, como o PostgreSQL o devolve, sem reserializar. A métrica `outbox.failed.messages` conta as mensagens com 5 ou mais tentativas entre as 1.000 pendentes mais antigas. Uma mensagem venenosa continua sendo reivindicada, nunca apagada, com alerta na quinta tentativa. O fluxo completo, com o SQL, está em [Fluxo: publicação do outbox](../../06-fluxos/publicacao-do-outbox.md).

## Alternativas descartadas

- Circuito em volta do lote, com a reivindicação dentro do pipeline. É o mais simples e falha no cenário do broker bloqueado.
- Baixar o `MinimumThroughput` para 3. Muda o contrato do documento de arquitetura 0011 para consertar o que a contagem por mensagem já resolve.
- Testar o meio aberto com uma mensagem de negócio. A mais antiga da fila pode ser justamente a que o broker recusa sempre, e o circuito nunca fecharia. A sonda própria separa a saúde do broker da qualidade das mensagens.
- Um disjuntor feito à mão, sem o Polly. A biblioteca já está no projeto, e o que faltava, contar por mensagem e sondar, se resolve em volta dela.
- Apagar a mensagem depois de N tentativas. Perde o evento de um lançamento sem ninguém ver.

## Consequências

Broker parado ou bloqueado não consome tentativa de mensagem nenhuma, e a volta dele esvazia o acúmulo de uma vez. O preço é código a mais do que a biblioteca daria de graça. A contagem de falhas olha só as 1.000 pendentes mais antigas, então uma mensagem venenosa além dessa janela escapa dela, mas nesse caso o alerta de idade do outbox já disparou e o problema é outro. Enquanto o broker não volta, a sonda deixa uma publicação vazia a cada 30 segundos, quando o circuito passa a meio aberto. Se um consumidor precisar do corpo com a ordem das chaves estável, ou se uma regra de negócio mandar parar a reivindicação depois de N tentativas, a publicação do corpo e a política de tentativas precisam ser revistas.
