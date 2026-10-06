# Documentação de arquitetura do ledger

A documentação descreve o ledger de um banco digital: o que ele garante, como é construído, como se comporta quando algo falha e como se opera. O texto está em português do Brasil. Código, tabelas, colunas, rotas, JSON e códigos de erro ficam em inglês, entre crases quando aparecem no meio da prosa. Os exemplos usam reais, e os instantes entram com fuso horário e saem em UTC.

As páginas descrevem o comportamento que o código tem e, quando há, citam o teste que o exercita. O que não foi medido nem exercitado está dito sem rodeio em [Limites conhecidos](09-qualidade/limites-conhecidos.md).

## Mapa

| Pasta | O que há nela | Comece por |
|---|---|---|
| `01-visao-geral/` | O ledger em uma leitura e o vocabulário comum | [Visão geral do ledger](01-visao-geral/visao-geral.md) |
| `02-contexto-e-requisitos/` | Por que existe, o que faz e dentro de que limites | [Contexto de negócio](02-contexto-e-requisitos/contexto-de-negocio.md) |
| `03-principios-e-decisoes/` | Princípios, riscos, questões em aberto e um documento de arquitetura por decisão | [Princípios de arquitetura](03-principios-e-decisoes/principios-de-arquitetura.md) |
| `04-modelos-c4/` | O sistema em quatro níveis de zoom, mais a implantação. O modelo formal está em `workspace.dsl` | [C4 nível 2: containers](04-modelos-c4/nivel-2-containers.md) |
| `05-contratos/` | API, erros, eventos, dados e configuração. O contrato legível por máquina é `openapi.v1.json` | [Contrato da API REST](05-contratos/api-rest.md) |
| `06-fluxos/` | Cada operação passo a passo, com o SQL de cada passo | [Índice dos fluxos](06-fluxos/README.md) |
| `07-consistencia-e-seguranca/` | Invariantes, ameaças, identidade e dados pessoais | [Modelo de consistência](07-consistencia-e-seguranca/modelo-de-consistencia.md) |
| `08-resiliencia-e-operacao/` | Falhas, observabilidade, alertas, procedimentos e capacidade | [Cenários de falha](08-resiliencia-e-operacao/cenarios-de-falha.md) |
| `09-qualidade/` | Estratégia de testes, testes de concorrência e de falha, verificações de build e o que não foi medido | [Estratégia de testes](09-qualidade/estrategia-de-testes.md) |
| `10-implantacao-e-entrega/` | Ambientes, execução local e pipelines | [Execução local](10-implantacao-e-entrega/execucao-local.md) |
| `11-evolucao/` | O que ficou fora da primeira versão e o gatilho de cada item | [Evolução futura](11-evolucao/evolucao-futura.md) |
| `12-engenharia/` | Convenções de código, de testes e de estrutura em camadas | [Convenções de código](12-engenharia/convencoes-de-codigo.md) |

As decisões de arquitetura estão em `03-principios-e-decisoes/documento-arquitetura/`, com o [índice](03-principios-e-decisoes/documento-arquitetura/README.md) na frente.

## Por onde ler

Quem tem pouco tempo lê a [visão geral](01-visao-geral/visao-geral.md), os [containers](04-modelos-c4/nivel-2-containers.md), os [princípios](03-principios-e-decisoes/principios-de-arquitetura.md) e os [limites conhecidos](09-qualidade/limites-conhecidos.md), nessa ordem. Para avaliar o desenho a fundo, o caminho é o [modelo de consistência](07-consistencia-e-seguranca/modelo-de-consistencia.md), os fluxos de [registro de lançamento](06-fluxos/registro-de-lancamento.md) e de [repetição idempotente](06-fluxos/repeticao-idempotente.md) e os [cenários de falha](08-resiliencia-e-operacao/cenarios-de-falha.md).

Quem vai integrar outro sistema precisa do [contrato da API](05-contratos/api-rest.md), do [catálogo de erros](05-contratos/catalogo-de-erros.md) e da página de [idempotência](05-contratos/idempotencia-e-hash-canonico.md). Quem opera começa por [saúde e observabilidade](08-resiliencia-e-operacao/saude-e-observabilidade.md) e pelos [procedimentos](08-resiliencia-e-operacao/runbooks.md). Quem desenvolve segue pelos componentes do [C4](04-modelos-c4/README.md), pelo [modelo de dados](05-contratos/modelo-de-dados.md) e pelas [convenções](12-engenharia/convencoes-de-codigo.md).

## Onde procurar cada assunto

| Assunto | Página |
|---|---|
| Rotas, cabeçalhos, escopos e corpos | [Contrato da API REST](05-contratos/api-rest.md) |
| Códigos de erro | [Catálogo de erros](05-contratos/catalogo-de-erros.md) |
| Tabelas, índices, papéis e privilégios | [Modelo de dados](05-contratos/modelo-de-dados.md) |
| Evento e topologia do broker | [Contrato de eventos](05-contratos/eventos.md) |
| Configuração e valores padrão | [Configuração e linha de comando](05-contratos/configuracao.md) |
| Algoritmo e SQL de cada operação | Páginas de `06-fluxos/` |
| Decisão, alternativas e consequências | O documento de arquitetura correspondente |
| Metas e requisitos | [Requisitos funcionais](02-contexto-e-requisitos/requisitos-funcionais.md) e [Requisitos não funcionais](02-contexto-e-requisitos/requisitos-nao-funcionais.md) |
| Métricas exportadas | [Catálogo de métricas](08-resiliencia-e-operacao/catalogo-de-metricas.md) |
| O que não foi medido nem exercitado | [Limites conhecidos](09-qualidade/limites-conhecidos.md) |

## Identificadores

Os requisitos usam `FR-nn` (funcional), `NFR-nn` (não funcional), `BR-nn` (regra de negócio) e `ASR-nn` (significativo para a arquitetura), e as decisões são numeradas com quatro dígitos (`0001`). Os cenários de falha vão de 1 a 16, e os testes de concorrência e de falha que não podem ser desligados levam `C1` a `C7` e `F1` a `F10`.

## Diagramas

Os diagramas são blocos Mermaid dentro da própria página, e o GitHub os renderiza. Fluxos usam `sequenceDiagram` e o modelo de dados, `erDiagram`. Os três primeiros níveis do C4 e as implantações usam `flowchart` com `subgraph`, pela legenda de [Modelos C4](04-modelos-c4/README.md), e o nível 4, `classDiagram`.

## Conferências automáticas

Parte do que as páginas afirmam é conferida por teste contra o código: o documento OpenAPI, o catálogo de métricas, o SQL das páginas de fluxo, a matriz de rastreabilidade, as classes de teste citadas e os links relativos. A lista, com o comando de cada uma, está em [Garantias automáticas](09-qualidade/portoes-de-qualidade.md).
