# Documento de arquitetura 0011: Resiliência

## Contexto

O sistema se propõe a RPO zero, 99,95% de disponibilidade e p99 de escrita de 150 ms, e nenhuma dessas metas foi provada em escala nem na topologia de produção ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md)). As falhas previsíveis são conta quente com fila no lock, pool esgotado, PostgreSQL lento, RabbitMQ fora, chamador acima do combinado e deploy no meio do tráfego. A nova tentativa é segura porque a idempotência é obrigatória ([documento de arquitetura 0006](0006-idempotencia-por-chave-e-hash.md)): repetir uma escrita devolve o resultado já gravado, não um segundo lançamento.

## Decisão

Os timeouts são em camadas, cada um menor que o de quem o envolve: `lock_timeout` de 1 s para a espera pela linha da conta, 2 s para o comando no Npgsql (1 s nas leituras), `statement_timeout` de 2,5 s na escrita e 1,5 s nas leituras como segunda barreira no servidor, 3 s para a requisição inteira e 1 s para obter conexão do pool. Com p99 de 150 ms, passar de 1 s já é patológico. São valores iniciais, ainda não calibrados sob carga ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md)).

A nova tentativa usa Polly 8 só no acesso a dados de escrita: até duas retentativas, backoff exponencial com jitter a partir de 50 ms, apenas para erro transitório de conexão, deadlock `40P01`, falha de serialização `40001`, desligamento administrativo `57P01`, servidor ainda sem aceitar conexões `57P03`, sessão derrubada por ficar parada dentro da transação `25P03` e a classe `08`. Se a conexão cai no commit e não se sabe se gravou, repetir com a mesma `Idempotency-Key` resolve. Nunca há nova tentativa em validação, saldo insuficiente, conflito de idempotência, pool esgotado ou timeout: os dois últimos sinalizam sobrecarga, que a repetição agravaria.

O circuit breaker existe só no Worker, ao redor da publicação: abre com 50% de falhas numa janela de 30 s e no mínimo 10 operações, fica aberto por 30 s e então testa com uma tentativa. Aberto, o Worker não reivindica lotes e as mensagens ficam seguras no outbox ([documento de arquitetura 0029](0029-publicador-do-outbox-com-circuito-por-mensagem.md)). A API não tem breaker de broker porque não fala com o broker, e não há breaker para o PostgreSQL, porque não existe alternativa a ele.

O limite de taxa usa o `RateLimiting` do ASP.NET Core, particionado pelo `client_id`, com balde de fichas por chamador e por tipo de operação e um limite adicional por conta nas escritas, que protege a fila no lock. Um limite de concorrência vem antes de todos ([documento de arquitetura 0027](0027-limites-de-taxa-e-de-concorrencia-em-cadeia.md)). Cota excedida volta 429 `RATE_LIMITED` com `Retry-After`, e saturação volta 503 `SERVICE_UNAVAILABLE`. Escrita, saldo e extrato usam fontes de dados Npgsql distintas, cada uma com seu pool, para que 10.000 leituras por segundo não esgotem as conexões das 2.000 escritas.

`/health/live` não consulta dependência alguma. `/health/ready` executa `SELECT 1` com timeout de 1 s e não inclui o RabbitMQ, porque broker fora não pode tirar a API do ar ([documento de arquitetura 0019](0019-readiness-nao-depende-do-provedor-de-chaves.md)). No `SIGTERM` a readiness falha e as requisições em andamento têm 30 s para terminar.

## Alternativas descartadas

- Nova tentativa na malha de serviço ou no gateway. Não sabe quais operações são idempotentes e não existe no `docker compose`.
- Nenhuma nova tentativa no servidor. Uma queda de conexão transitória viraria erro para o cliente à toa.
- Limite de taxa global com Redis. Poria uma dependência nova no caminho de toda requisição.
- Implantações separadas de leitura e escrita. Dá isolamento mais forte, mas fica fora da primeira versão.

## Consequências

O limite de taxa vive na memória de cada instância, então com N instâncias o teto efetivo é N vezes o configurado. Serve como proteção, não como cota contratual, e uma cota global exigiria gateway ou Redis. A soma dos pools das instâncias e do Worker precisa caber em `max_connections` ([Capacidade e escala](../../08-resiliencia-e-operacao/capacidade-e-escala.md)).

Mais de 1% das escritas precisando de nova tentativa é sinal de contenção e não de falha transitória, e mais tentativas só agravariam.
