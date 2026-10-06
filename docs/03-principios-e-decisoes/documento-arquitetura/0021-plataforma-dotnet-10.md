# Documento de arquitetura 0021: Plataforma .NET 10

## Contexto

Um sistema financeiro que vai guardar lançamentos por dez anos precisa de um runtime com suporte longo pela frente. O .NET 8 (LTS) e o .NET 9 (suporte padrão) terminam o suporte na mesma data, 10 de novembro de 2026. O .NET 10 é LTS e tem suporte até 14 de novembro de 2028. Além do prazo, o .NET 8 não tem `Guid.CreateVersion7`, que o ledger usa para os identificadores ([documento de arquitetura 0020](0020-identificadores-uuid-v7.md)).

## Decisão

A solução usa .NET 10 com C# 14. O `Directory.Build.props` define `net10.0` para todos os projetos, e o `global.json` pede o SDK 10.0.100 ou um mais novo da mesma série, sem pré-lançamento (`rollForward: latestFeature`). Os pacotes que acompanham o framework ficam na linha 10.x e os demais em versões estáveis, todos com a versão centralizada em `Directory.Packages.props`. Os Dockerfiles usam as imagens `sdk:10.0` e `aspnet:10.0`, fixadas por digest, e o workflow do GitHub Actions instala o SDK 10.

O build roda com `TreatWarningsAsErrors`, `AnalysisLevel` em `latest-all` e `AnalysisMode` em `All`, em todos os projetos, testes incluídos ([documento de arquitetura 0015](0015-sem-comentarios-e-zero-warnings.md)). Qualquer análise nova do SDK vira erro de build, então atualizar o SDK obriga a corrigir o que ele passar a apontar. A CA1515, que reclama de tipo público que poderia ser interno, fica ligada em `src/`, e os tipos que não precisam ser públicos são `internal`, visíveis aos testes por `InternalsVisibleTo`. Em `tests/**.cs` ela fica desligada, porque o xUnit exige classes, fixtures e coleções públicas ([Convenções de código](../../12-engenharia/convencoes-de-codigo.md)).

O projeto fica no `xunit` 2.9.3, mesmo marcado como preterido no NuGet em favor do `xunit.v3`. Migrar troca o modelo de execução dos projetos de teste e obriga a revalidar fixtures e coleções, sem ganho que pague o risco. Hoje é o único pacote preterido da solução.

## Alternativas descartadas

- .NET 8. A LTS mais antiga acaba na mesma data do .NET 9, e o projeto precisaria de classe própria para UUID v7.
- .NET 9. Tem o `Guid.CreateVersion7`, mas o suporte padrão termina em 10 de novembro de 2026, e o projeto teria de migrar para o .NET 10 logo em seguida.
- Multi-target com 8 e 10. Dobra a matriz de testes e de imagens por um benefício que escolher a versão certa já entrega.

## Consequências

O suporte do .NET 10 vai até 14 de novembro de 2028, e a migração para a LTS seguinte precisa entrar na agenda antes disso ([Evolução futura](../../11-evolucao/evolucao-futura.md)). Se uma dependência obrigatória não acompanhar a versão, esta escolha precisa ser revista.

O `xunit` 2.x pode deixar de receber correção de segurança. Se isso acontecer, a migração para o `xunit.v3` deixa de poder esperar.
