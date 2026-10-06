# Documento de arquitetura 0019: A readiness não depende do provedor de chaves

## Contexto

O que acontece com a API quando o cofre de chaves cai admite duas respostas razoáveis. Uma põe a verificação `keys` na readiness, com falha em 503. A outra a trata como `Degraded`, que responde 200 e chama alguém por alerta.

As chaves de dados pessoais só servem a quem cifra ou decifra o documento do titular, ou seja, à criação de conta e à leitura em claro. Registrar lançamento, consultar saldo e listar extrato nunca tocam em chave nenhuma. Se `keys` derrubar a readiness, uma queda do cofre tira todas as instâncias do balanceamento, as novas não sobem durante um deploy, e o dinheiro para de andar por causa de uma dependência que ele não usa. Isso contradiz o princípio de que o caminho do dinheiro independe da chave ([Princípios de arquitetura](../principios-de-arquitetura.md)).

## Decisão

A verificação `keys` olha se o provedor entrega o conjunto ativo, que só existe depois de o autoteste de cifra passar, e se alguma versão de chave sumiu da fonte desde a subida. A falha dela é `Degraded`, que responde 200, e não `Unhealthy`. A readiness da API fica com três causas de 503: o PostgreSQL não responde a um `SELECT` em 1 segundo, a versão do esquema é menor que a esperada e o processo recebeu o pedido de encerramento. A do Worker acrescenta o broker e o acúmulo do outbox, também como `Degraded`.

Com o cofre fora, só a rota que usa a chave sofre: `POST /v1/accounts` responde 503 `SERVICE_UNAVAILABLE` com `Retry-After`, e lançamento, saldo e extrato seguem. O alerta de `Degraded` em `keys` chama alguém, porque a criação de conta parada precisa ser vista.

Duas coisas impedem a subida, porque indicam configuração errada e não indisponibilidade: uma chave malformada ou ausente na configuração e o provedor por configuração fora de Development e Testing. O processo recusa iniciar com um erro que nomeia a chave e a regra, sem imprimir valor. Com a API em execução, o provedor mantém as chaves em memória e as relê a cada 10 minutos, e uma releitura que falha mantém as anteriores e registra o aviso.

## Alternativas descartadas

- `keys` com 503 na readiness. É mais simples de explicar e faz a pior coisa possível numa queda do cofre: para o ledger inteiro.
- Tirar `keys` da readiness e deixar só o log. Perde o sinal, e `Degraded` mantém o alerta sem tirar a instância do ar.
- Recusar a subida quando o cofre não responde. Impediria deploy durante uma queda, justamente quando se quer subir instâncias novas.
- Cache longo das chaves no processo para atravessar a queda. Ajuda, mas não substitui a decisão, porque a primeira instância a subir não teria cache.

## Consequências

A criação de conta pode falhar com 503 enquanto o resto responde normalmente, e quem opera precisa saber que `Degraded` na API quase sempre quer dizer isso. Como `/health/ready` responde 200 nesse estado, o balanceador não ajuda a detectar o problema, e o alerta é o que avisa.

O comportamento é exercitado contra um provedor de chaves que falha, nunca contra um cofre real: o `ApiReadinessWithDatabaseTests` confere a readiness `Degraded` com o cofre vazio e a criação de conta respondendo 503 enquanto o resto atende, e o `KeyProviderHealthCheckTests` fixa a verificação `keys`. O repositório oferece só um provedor que lê uma pasta de segredos montada no contêiner, e não existe adaptador para um cofre de chaves externo, então a degradação contra um cofre real não foi exercitada ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md)).
