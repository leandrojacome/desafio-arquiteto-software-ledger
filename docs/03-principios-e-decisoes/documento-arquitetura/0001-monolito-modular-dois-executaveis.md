# Documento de arquitetura 0001: Monolito modular com dois executáveis

## Contexto

Cada lançamento atualiza o saldo, grava a linha do ledger e registra o evento de integração na mesma transação, com meta de 2.000 escritas por segundo e p99 de 150 ms ([Requisitos não funcionais](../../02-contexto-e-requisitos/requisitos-nao-funcionais.md)). A invariante que importa, o saldo ser consequência exata dos lançamentos, vive inteira no banco relacional. Publicar eventos e conferir a integridade toleram atraso. O domínio é um só e a equipe é pequena, então a pergunta era onde traçar as fronteiras: na rede ou dentro do processo.

## Decisão

Um monolito modular com cinco projetos (`Ledger.Domain`, `Ledger.Application`, `Ledger.Infrastructure`, `Ledger.Api`, `Ledger.Worker`) e dependência sempre para dentro: o domínio não referencia ninguém, a aplicação declara as portas, a infraestrutura as implementa, e só a API e o Worker enxergam a infraestrutura. O `LayerDependencyTests` derruba a suíte se alguém cruzar a linha.

Da mesma base saem dois executáveis, ambos com réplicas. A API atende HTTP e não guarda estado. O Worker publica o outbox e roda a conferência de integridade. Os dois falam com o mesmo PostgreSQL e não falam entre si. O publicador fica fora da API porque o broker é o componente mais provável de ficar lento, e essa lentidão não pode disputar threads e conexões com as requisições.

## Alternativas descartadas

- Microsserviços. A invariante do saldo passaria a cruzar a rede, e sobrariam a transação distribuída ou a saga, que num ledger significa estornos automáticos para consertar uma escolha de arquitetura.
- Executável único. Seria a escolha se o broker fosse confiável. Perdeu pelo acoplamento de falha e porque escalar a API para absorver leitura multiplicaria também os pollers do outbox.
- Funções serverless. Cada instância abre as próprias conexões, e numa rajada o `max_connections` do PostgreSQL acaba antes da CPU.
- Serviço de leitura separado. O saldo é uma busca por índice ([documento de arquitetura 0003](0003-saldo-apos-em-cada-lancamento.md)), e dividir um serviço para isso criaria um problema de consistência que hoje não existe.

## Consequências

A transação de banco é a única ferramenta de consistência, e a corretude do saldo é do SQL, não de um protocolo entre serviços. O preço é um banco compartilhado que é o gargalo real, fronteiras que dependem de disciplina e de teste, e uma API que escala como um bloco. Se surgir um segundo domínio com ciclo de vida próprio, ou se a leitura pedir escala independente, o primeiro passo é uma réplica de leitura ([Evolução futura](../../11-evolucao/evolucao-futura.md)), não um serviço novo.
