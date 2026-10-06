# Documento de arquitetura 0015: Código sem comentários e build sem warnings

## Contexto

Duas regras deste projeto são mais rígidas que a prática comum: nenhum arquivo C# tem comentário, e a solução compila sem warnings.

Comentário envelhece. O compilador não o lê, nenhum teste o contradiz, e a cada mudança ele fica um pouco mais falso sem ninguém perceber. O que descreve o que o código faz repete o código, e o que explica o porquê fica preso a um arquivo onde ninguém procura. Quando o código precisa de comentário para ser entendido, o problema costuma estar num nome, no tamanho do método ou num tipo que faltou. Warning é o compilador dizendo que algo parece errado, e onde se aprende a ignorá-lo o ruído cresce até esconder o aviso que importava: uma desreferência de possível nulo num caminho de dinheiro, ou um `CancellationToken` que não é repassado e deixa uma requisição cancelada segurando uma conexão.

## Decisão

O código C# de `src/` e `tests/` não tem comentário de nenhuma forma. Ele se explica por nomes, métodos pequenos, tipos de domínio (`Money`, `IdempotencyKey`) e testes cujo nome conta o comportamento, a explicação que não envelhece porque quebra quando fica falsa. O porquê das decisões vive em `docs/` e nos documentos de arquitetura. A documentação das rotas é código: `.WithSummary(...)`, `.WithDescription(...)` e `.Produces<T>(...)` alimentam o OpenAPI no lugar do XML doc, e o `OpenApiContractTests` compara o documento versionado com o que a API gera ([Contrato da API REST](../../05-contratos/api-rest.md)). Pendência não vira anotação no fonte: mora em [Questões em aberto](../questoes-em-aberto.md) ou em [Evolução futura](../../11-evolucao/evolucao-futura.md).

O build não tem warnings. O `Directory.Build.props` liga `TreatWarningsAsErrors`, `Nullable`, `EnforceCodeStyleInBuild` e o nível de análise máximo em todos os projetos, testes incluídos. Supressão pontual é por atributo `SuppressMessage` com `Justification`, e `#pragma warning disable` não entra. O `BannedSymbols.txt` proíbe também `DateTime.UtcNow`, `Thread.Sleep` e `ConfigureAwait` em todos os projetos e `Guid.NewGuid` nos de produção, cada um com o motivo na própria linha.

O estilo é conferido por `dotnet format --verify-no-changes` ([Portões de qualidade](../../09-qualidade/portoes-de-qualidade.md)), e o `RoslynSyntaxGuardTests` reprova `#pragma` no código e nos testes e o operador `!` de supressão de nulabilidade no código.

## Alternativas descartadas

- Permitir `///` na API pública, como manda a convenção do .NET. Duplica a assinatura e envelhece, e o contrato está no OpenAPI versionado e nas páginas de contrato.
- Permitir só comentário de porquê. A fronteira entre o porquê e o quê é questão de gosto, e uma regra binária é mais simples de seguir.
- `WarningsAsErrors` só para um subconjunto de regras. Deixa a cauda longa de avisos que esconde o importante.
- `TreatWarningsAsErrors` só no CI. A pessoa descobriria o problema tarde, depois do envio.

## Consequências

O custo é real: o trecho genuinamente não óbvio, como o contorno de um comportamento do driver, não tem onde morar ao lado do código. A saída é extrair um método ou constante cujo nome diga o motivo, escrever um teste que fixe o comportamento e registrar o porquê em documento de arquitetura. Atualizar pacote pode trazer analisador novo e quebrar o build, e a correção vem junto com a atualização.
