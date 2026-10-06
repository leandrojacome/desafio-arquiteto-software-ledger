# 05 Contratos

Esta pasta descreve o que o ledger expõe a quem o chama e a quem o opera. O passo a passo interno de cada operação está em [06 Fluxos](../06-fluxos/README.md).

- [API REST](api-rest.md): rotas, cabeçalhos, formatos, datas e fusos horários.
- [Catálogo de erros](catalogo-de-erros.md): códigos, status, mensagens e razões de validação.
- [Eventos](eventos.md): o `EntryRegistered` publicado no broker, o envelope e a topologia.
- [Modelo de dados](modelo-de-dados.md): tabelas, índices, imutabilidade e privilégios.
- [Configuração e linha de comando](configuracao.md): chaves, regras por ambiente e códigos de saída.
- [Idempotência e hash canônico](idempotencia-e-hash-canonico.md): a chave de idempotência e o cálculo do hash do pedido.
- [Cursor do extrato](cursor-do-extrato.md): formato e verificação do cursor de paginação.
- [openapi.v1.json](openapi.v1.json): o mesmo contrato da API REST, legível por máquina.
