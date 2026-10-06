# Documento de arquitetura 0022: Testes que dependem de ambiente falham por padrão

## Contexto

Parte dos testes precisa de Docker (PostgreSQL e RabbitMQ por Testcontainers) ou da pilha do Compose no ar (o fim a fim). Se esse teste for ignorado quando o ambiente falta, `dotnet test` termina com sucesso sem ter exercitado a camada que prova o ledger, e quem roda na própria máquina não repara num ignorado perdido no meio da saída. No CI isso não pode acontecer ([documento de arquitetura 0016](0016-azure-devops-como-plataforma-de-ci-cd.md)). Há ainda um risco de borda: um `LEDGER_E2E_BASE_URL` mal escrito não pode virar, em silêncio, o endereço padrão, porque o teste passaria a falar com outra pilha.

## Decisão

Teste que precisa de ambiente falha quando o ambiente falta, em qualquer máquina, e a mensagem diz o que fazer.

Na integração, `DockerFact` e `DockerTheory` só ignoram o teste com `LEDGER_TESTS_DISABLE_DOCKER=true`. Sem essa variável, o atributo nem consulta o Docker durante a descoberta. O `PostgresFixture` sonda o daemon uma vez e, se ele não responde, lança uma exceção que explica como subir o Docker e como ignorar de propósito.

No fim a fim, `E2EFact` só ignora com `LEDGER_SKIP_E2E=true`, e o motivo vai escrito no resultado. O fixture da coleção resolve `LEDGER_E2E_BASE_URL`: a variável ausente usa `http://localhost:8080`, e um valor que não seja URL absoluta `http` ou `https` lança exceção em vez de cair no padrão. Em seguida espera até 60 segundos por `/health/ready` e, se a pilha não responde, falha dizendo como subi-la (`docker compose up -d --build --wait`), como apontar a variável para uma pilha que já está de pé e como pedir que o teste suba a própria, com `LEDGER_E2E_PROVISION=true` ([documento de arquitetura 0032](0032-teste-fim-a-fim-que-provisiona-o-proprio-ambiente.md)).

`LEDGER_REQUIRE_DOCKER=true` desconsidera os dois opt-outs: com ela, nenhum teste de ambiente é ignorado, mesmo que alguém tenha definido `LEDGER_TESTS_DISABLE_DOCKER` ou `LEDGER_SKIP_E2E`. O Azure Pipelines e o GitHub Actions a definem, e o `CiPipelineTests` e o `GitHubWorkflowTests` conferem isso. Quem não tem Docker roda a unidade e a arquitetura com `dotnet test --filter "FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~EndToEnd"`, que deixa de fora os dois projetos que dependem de ambiente.

## Alternativas descartadas

- Ignorado como padrão e `LEDGER_REQUIRE_DOCKER=true` só no CI. Depende de cada pessoa lembrar de ligar a variável, e a regra fica na memória de quem escreve em vez de ficar no código.
- Fixture que sobe o Compose sozinho quando não há pilha. Tira o passo manual, mas esconde minutos de custo dentro de um teste. O [documento de arquitetura 0032](0032-teste-fim-a-fim-que-provisiona-o-proprio-ambiente.md) oferece isso como modo opcional.
- Testes de ambiente marcados como explícitos, que só rodam quando pedidos. O comando comum ficaria verde sem exercitar a integração, que é o problema.

## Consequências

Quem não tem Docker precisa declarar isso, pela variável, para rodar o resto da suíte. Quem esquece vê vários testes vermelhos com a mesma mensagem, o que incomoda de propósito. O fim a fim pode levar até um minuto para falhar quando a pilha não está de pé, e quem sabe que não vai subir o ambiente evita a espera com `LEDGER_SKIP_E2E=true`. O teste ignorado continua existindo, mas só por escolha explícita e com a razão no resultado. Se pessoas sem Docker passarem a contribuir com frequência, vale um perfil documentado por categoria no lugar do opt-out por variável.
