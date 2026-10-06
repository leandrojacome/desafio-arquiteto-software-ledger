# Documento de arquitetura 0037: Idioma das mensagens ao chamador

## Contexto

Quem chama o ledger são sistemas de um banco brasileiro, e quem lê as respostas de erro são pessoas da mesma empresa: analistas de back-office que conferem um lançamento recusado, integradores que escrevem o cliente e o plantão que copia o `detail` para um chamado. Mensagens em inglês obrigam cada uma dessas pessoas a traduzir, e a documentação e os valores dos contratos já são brasileiros.

## Decisão

O texto que uma pessoa lê é português do Brasil: o `title` e o `detail` do Problem Details, a `message` de cada item de `errors`, o texto de `Error.Message` de Domain e Application quando chega ao chamador (porque vira o `detail`) e as descrições do documento OpenAPI. O tom é profissional e neutro, em frases completas terminadas em ponto, com o vocabulário do [Glossário](../../01-visao-geral/glossario.md).

O que um programa lê continua em inglês: códigos de erro (`INSUFFICIENT_FUNDS`), razões por campo (`TOO_MANY_DECIMALS`), nomes de campo, de parâmetro, de cabeçalho e de rota, valores de enumeração, tokens de protocolo, logs, rótulos de métrica, nomes de evento, exceções de programação e falhas de validação de configuração. Nenhuma mensagem repete um valor recebido.

Os exemplos de código, de teste e de documentação usam reais (`BRL`, "R$"), com CPF e CNPJ de dígitos verificadores válidos. O euro (`EUR`) só aparece onde uma segunda moeda é indispensável para exercitar moeda não suportada ou divergente.

A resposta sai em UTF-8 com as letras acentuadas legíveis: o codificador do `System.Text.Json` não escapa o alfabeto latino, e `application/problem+json` declara `charset=utf-8`. O codificador é o de faixas (Latin básico, Latin-1, Latin estendido A, pontuação geral e símbolos de moeda), e não o `UnsafeRelaxedJsonEscaping`, para que o texto enviado pelo chamador, como a `description`, continue com `<`, `>`, `&`, `'`, `"`, `+` e a crase escapados na resposta.

## Alternativas descartadas

- Mensagens em inglês, com a tradução a cargo do consumidor. Cada sistema manteria a própria tabela, que sai de sincronia com a API, e o texto que o plantão copia para um chamado continuaria difícil de ler. Quem decide por máquina já tem o `code`, que é estável.
- Negociar o idioma por `Accept-Language`. Resolveria um caso de mais de um idioma que não existe, já que todos os chamadores são do mesmo banco, ao custo de um catálogo de recursos por idioma.
- Traduzir também códigos, razões e nomes de campo. Quebraria o contrato e todo consumidor que decide por `INSUFFICIENT_FUNDS` ou lê `amount`.
- Traduzir logs e rótulos de métrica. Quem os lê é o operador, que busca por termo, e os rótulos são contrato com painéis e alertas.

## Consequências

O texto de uma mensagem é fixo no código, sem catálogo de recursos, e os testes de catálogo travam regras de forma: sem aspas duplas, sem chaves, com ponto final, distinta das demais da mesma classe e sem dígito nas mensagens de conta. Mudar uma frase exige mudar o teste junto. O chamador decide pelo `code` e pelo status, e o texto pode mudar entre versões sem aviso de incompatibilidade. As [Convenções de código](../../12-engenharia/convencoes-de-codigo.md) dizem como escrever uma mensagem nova, e o [Catálogo de erros](../../05-contratos/catalogo-de-erros.md) traz os textos exatos.

Se aparecer um chamador que precise de outro idioma ou de mostrar a mensagem ao cliente final do banco, o caminho é um catálogo por idioma escolhido por `Accept-Language`, sem mudar códigos, razões nem nomes de campo.
