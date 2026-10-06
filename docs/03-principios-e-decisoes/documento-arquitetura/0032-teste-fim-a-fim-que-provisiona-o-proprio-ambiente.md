# Documento de arquitetura 0032: Teste fim a fim que provisiona o próprio ambiente

## Contexto

O teste fim a fim precisa do que um teste de integração com contêiner descartável não dá: as imagens de verdade da API e do Worker, a criação dos papéis do banco, o `migrator`, a rede do Compose e a possibilidade de parar o PostgreSQL, o RabbitMQ, o Worker ou a API no meio do teste e subir de novo.

O [documento de arquitetura 0022](0022-testes-dependentes-de-ambiente-falham-por-padrao.md) faz o teste falhar quando a pilha não está de pé. Mas o ambiente de desenvolvimento do Compose tem dados, portas fixas (8080, 5432, 5672) e senhas fracas de propósito. Um teste que derruba o banco e apaga o volume não pode rodar contra ele sem aviso, e dois testes não podem dividir as mesmas portas.

## Decisão

O teste tem dois modos.

No modo de anexar, o padrão, ele se liga a uma pilha que já está de pé no Compose. As URLs vêm de `LEDGER_E2E_BASE_URL` e das variáveis irmãs, as credenciais vêm do `.env`, e os testes que param e iniciam serviços usam o projeto do Compose de `LEDGER_E2E_COMPOSE_PROJECT` (padrão `ledger-test`). As conexões com o banco que o teste abre, a do Worker e a de administrador (esta só no teste de integridade), seguem o `POSTGRES_PORT` do ambiente antes do `.env`, para o caso de a 5432 estar ocupada.

No modo de provisionar, ligado por `LEDGER_E2E_PROVISION=true`, o fixture da coleção sobe a própria pilha e a derruba no fim:

```bash
LEDGER_E2E_PROVISION=true dotnet test tests/Ledger.EndToEnd.Tests
```

Quem provisiona é o `E2EProvisioner`: um projeto do Compose com nome único (`ledger-e2e-` mais oito caracteres hexadecimais) sobre os mesmos `docker-compose.yml` e `docker-compose.test.yml`, cinco portas livres escolhidas na hora no endereço de loopback, senhas, chaves de dados pessoais e chave do cursor geradas ao acaso a cada execução, e um par de chaves de assinatura de token gerado na memória do teste, cuja chave pública vai para a API. A subida é um `up -d --build --wait` com prazo, e a derrubada, um `down --volumes --remove-orphans` que roda mesmo quando a subida falha. O `ComposeControl` expõe parar, iniciar e matar um serviço, e os testes de resiliência o usam nos dois modos.

## Alternativas descartadas

- Só anexar. É o mais simples e é o padrão, mas depende do estado da máquina: dados de execuções anteriores, portas ocupadas, um serviço que um teste anterior deixou parado.
- `Testcontainers` com o módulo de Compose. Esconderia o `docker compose` atrás de outra camada sem resolver a parte difícil, que é construir as imagens do repositório e esperar o `migrator` terminar. Chamar o Compose direto deixa o comando que falha igual ao que a pessoa rodaria à mão.
- Um comando externo que sobe a pilha, roda os testes e derruba. Perde a derrubada quando o processo de teste morre, e não deixa um teste isolado pedir a própria pilha.
- Provisionar sempre. Cada execução pagaria os minutos de construção das imagens, mesmo quando a pilha já está de pé e só se quer repetir um teste contra ela.

## Consequências

Quem quer rodar o fim a fim sem preparar nada roda um comando e não deixa nada para trás, nem toca no ambiente de desenvolvimento, e duas execuções ao mesmo tempo não se enxergam. O custo é a construção das imagens, que as camadas em cache reduzem mas não eliminam, e a CLI do Docker no `PATH`. As regras do documento de arquitetura 0022 valem nos dois modos: sem pilha e sem Docker, o teste falha dizendo o que fazer.

O modo de provisionar deixa de servir se a construção das imagens passar a dominar o tempo do fim a fim, ou se a execução migrar para uma máquina de build sem acesso ao daemon do Docker.
