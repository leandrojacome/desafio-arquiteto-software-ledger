# Documento de arquitetura 0016: CI/CD: Azure DevOps na entrega e GitHub Actions na validação

## Contexto

O ledger precisa de uma esteira que prove, a cada mudança, que o código compila sem avisos e passa nos testes numa máquina limpa, e que empacote e envie as imagens de contêiner. A entrega é no Azure DevOps: `azure-pipelines.yml` na raiz e modelos em `pipelines/`, resultados de teste e cobertura publicados no Azure Pipelines e imagens enviadas ao Azure Container Registry. Só que o código também mora no GitHub, e quem o abre lá não vê execução nenhuma do Azure DevOps. O resultado dos testes ficaria dentro de um serviço que nem todo leitor consegue abrir.

Um pipeline só vale se for difícil de divergir do que as pessoas rodam localmente. Quando ele contém lógica própria, vira um segundo sistema para manter, e a falha dele só se reproduz no serviço.

## Decisão

Há dois pipelines, com papéis diferentes. O Azure DevOps (`azure-pipelines.yml` e `pipelines/`) é a plataforma de entrega: faz a validação, roda os testes, empacota e envia as imagens ao Azure Container Registry. O GitHub Actions (`.github/workflows/ci.yml`) existe para o resultado aparecer no repositório: roda a validação, os testes de unidade e de arquitetura e os de integração, e não empacota nem publica nada. O que cada um executa, passo a passo, está em [Pipeline no Azure DevOps](../../10-implantacao-e-entrega/pipeline-azure-devops.md) e em [GitHub Actions](../../10-implantacao-e-entrega/github-actions.md).

Nenhum dos dois tem lógica própria. Cada passo chama um comando direto, como `dotnet` ou `docker compose`, o mesmo que se roda na máquina de quem escreveu ([Execução local](../../10-implantacao-e-entrega/execucao-local.md)), então a falha de um pipeline se reproduz fora dele. Para os dois não divergirem, o `GitHubWorkflowTests` falha se o workflow rodar um comando que o pipeline do Azure DevOps não roda, e o `CiPipelineTests` confere a estrutura do pipeline.

Os testes que dependem de Docker rodam em modo estrito, com `LEDGER_REQUIRE_DOCKER=true`: sem Docker o teste falha em vez de ser ignorado, porque um verde que não executou nada é pior que um vermelho ([documento de arquitetura 0022](0022-testes-dependentes-de-ambiente-falham-por-padrao.md)).

## Alternativas descartadas

- GitHub Actions como única plataforma. Mostraria o resultado ao lado do código, mas a entrega é no Azure Container Registry, com aprovação em `environment`, e refazer isso em outra plataforma não traz ganho.
- Azure DevOps como única plataforma. Mantém uma só, mas deixa o repositório sem prova visível de que o código compila e passa nos testes.
- Replicar todas as etapas nos dois. Dobraria a manutenção para um ganho de visibilidade que a validação já entrega.
- Nenhum pipeline até o repositório estar ligado ao serviço. Deixaria sem prova o que a documentação promete numa máquina limpa, e o pipeline faz parte do que se entrega.

## Consequências

A validação roda em dois lugares, e o custo de mantê-los iguais fica nos testes que comparam os comandos. Mudar o que um comando faz não exige mexer em nenhum dos arquivos, mas mudar a lista de passos exige mexer nos dois.

Os testes de estrutura conferem os arquivos, mas o esquema do Azure Pipelines só é validado por inteiro pelo próprio serviço, e o workflow só roda de fato no GitHub. Os tipos de passo e as propriedades aceitas em cada um só se provam na primeira execução real ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md)).
