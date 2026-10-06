# Documento de arquitetura 0012: Observabilidade

## Contexto

Com meta de RPO zero e de 99,95% de disponibilidade, saber que o serviço caiu não basta. É preciso responder em minutos se um lançamento gravou, por que uma conta está lenta e se o outbox está atrasado. Os chamadores são sistemas, não pessoas, e uma falha silenciosa pode demorar dias para virar chamado.

## Decisão

Os logs usam `ILogger` com mensagens `[LoggerMessage]` e Serilog como provedor, em JSON no stdout, enriquecidos com `CorrelationId`, `TraceId`, `SpanId`, nome do serviço e ambiente. Há um resumo por requisição, cujo nível depende do resultado: 2xx rápida em `Debug`, 4xx em `Information`, 5xx e exceção em `Error`, 2xx acima de 150 ms em `Warning` e `/health/*` em `Verbose`. A leitura bem-sucedida fica em `Debug` porque dez mil linhas por segundo de resumo de saldo não dizem nada que a métrica não diga. O código só escreve no stdout, e o destino é de quem opera. Documento do titular e corpo de requisição nunca são logados, e a política de destructuring mascara o que escapar ([documento de arquitetura 0010](0010-seguranca-jwt-e-criptografia-de-pii.md)).

A correlação usa o cabeçalho `X-Correlation-Id`. Se o chamador manda um valor de 8 a 64 caracteres entre letras, dígitos, ponto, dois-pontos, hífen e sublinhado, a API o aceita. Sem valor, ou com um fora do padrão, a API gera outro. Ele volta na resposta, entra no escopo do log, vai no corpo dos erros e é gravado com a mensagem do outbox, para o consumidor continuar a mesma investigação. O `traceparent` do W3C convive com ele porque chamadores legados não o propagam, e o identificador de correlação é o que cabe num chamado de suporte.

Traces e métricas usam o SDK do OpenTelemetry com exportação OTLP: instrumentação de ASP.NET Core, HttpClient e Npgsql, mais um `ActivitySource` próprio para os casos de uso e para a publicação no RabbitMQ, com `traceparent` no cabeçalho da mensagem para o consumidor continuar o trace. As métricas próprias cobrem lançamentos por tipo e resultado, a latência da escrita e do saldo e, a mais importante para o Worker, a idade da mensagem pendente mais antiga do outbox.

A amostragem de traces é `ParentBased` e a taxa é configuração de implantação, pelas variáveis padrão do OpenTelemetry. Sem variável, o SDK amostra tudo. Para produção, recomendo `parentbased_traceidratio` com 0,1. Todo log de erro carrega o `TraceId`, então o erro está no log mesmo quando o trace foi descartado. A amostragem de cauda exigiria que o SDK enviasse tudo ao coletor e não está prevista. O identificador da conta nunca vira rótulo de métrica, porque são milhões de valores e a série explode: ele vai como atributo de span e campo de log.

## Alternativas descartadas

- SDK do Application Insights ou APM proprietário. Prende a plataforma a um fornecedor, e o OpenTelemetry fala com eles via OTLP quando o banco quiser.
- Formatador JSON do `ILogger` do .NET. Serve, mas o Serilog já entrega destructuring com mascaramento, enriquecedores e o resumo por requisição.
- `prometheus-net` só para métricas. Resolve metade e deixa os traces para outro sistema de instrumentação.
- Logs em texto simples. Filtrar por conta ou correlação vira regex.
- Só identificador de correlação, sem traces. Não mostra se o tempo da requisição foi SQL, lock ou serialização.

## Consequências

Em produção é preciso um coletor OpenTelemetry e um destino para logs, traces e métricas, escolha de quem opera. Se o custo de armazenamento passar do aceitável, reduzem-se a amostragem e o nível dos logs antes de trocar de ferramenta. Com 10% de amostragem um trace específico pode não existir, mas todo erro continua no log, com o `TraceId` e o identificador de correlação.

A idade do outbox é o indicador que mais se aproxima de um objetivo de nível de serviço para a entrega dos eventos: se sobe sem a escrita falhar, o problema é o Worker ou o broker ([Cenários de falha](../../08-resiliencia-e-operacao/cenarios-de-falha.md)).
