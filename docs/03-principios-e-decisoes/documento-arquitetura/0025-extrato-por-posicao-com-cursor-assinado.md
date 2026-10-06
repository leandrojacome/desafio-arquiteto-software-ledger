# Documento de arquitetura 0025: Extrato por posição, com cursor assinado e limite superior único

## Contexto

O extrato lista os lançamentos de uma conta do mais novo ao mais antigo, em páginas. A paginação não pode degradar com a profundidade e precisa continuar coerente enquanto a conta recebe escritas. O caso que dá o tom é a conciliação diária, que pede a janela de ontem em páginas de até 200 itens numa conta muito movimentada. Três coisas pediam decisão: como paginar sem custo crescente, o que o chamador recebe para pedir a página seguinte e como o extrato e o saldo em um instante dizem a mesma coisa nas fronteiras de uma janela.

## Decisão

A página seguinte é pedida por posição, não por deslocamento. A posição de um lançamento é o par `(recorded_at, account_version)`, e a ordem do extrato é a do índice `ix_ledger_entries_account_id_recorded_at_account_version` lido ao contrário. O chamador recebe um cursor opaco, `nextCursor`, nulo na última página.

O cursor tem 33 bytes: a versão do formato, o `recorded_at` em microssegundos, a posição da conta e os 16 primeiros bytes de um HMAC-SHA-256 sobre o identificador da conta e os bytes anteriores, em base64url. A chave é `Security:Cursor:SigningKey`, de 32 bytes, e a API não sobe sem ela. Toda falha de verificação (tamanho, alfabeto, versão, assinatura, conta errada) vira o mesmo 400 `VALIDATION_FAILED` com a razão `INVALID_CURSOR`, sem dizer qual passo falhou. O cursor não é segredo, porque a posição é do próprio chamador. A assinatura existe para o ledger não confiar numa posição fabricada ou levada de outra conta ([Cursor do extrato](../../05-contratos/cursor-do-extrato.md)).

O SQL usa um único limite superior, o menor entre o cursor e o `to`, calculado pelo domínio em `StatementBounds.Resolve`. A identidade entre extrato e saldo vale com o ajuste de um microssegundo: a soma dos valores com sinal de `[from, to)` é `saldo(to - 1 µs) - saldo(from - 1 µs)`, e sem o ajuste ela só vale quando nenhum lançamento cai exatamente em `from` ou em `to`.

## Alternativas descartadas

- Deslocamento (`OFFSET`). Custa mais a cada página e repete ou pula itens quando entram lançamentos no topo, o que num extrato lido durante a escrita é defeito de correção.
- Três limites no SQL (`to`, cursor e comparação de linha). O planejador deixa de ter um limite simples sobre `recorded_at`, e a primeira página de uma janela do começo do histórico passa a ler muito mais blocos que a página do topo. Com um limite só, a leitura fica em poucos blocos em qualquer posição, e o `QueryPlanTests` guarda isso.
- Cursor sem assinatura, em JWT ou com `IDataProtector`. Sem assinatura, qualquer um fabrica posições. O JWT custa tamanho e superfície sem ganho. O `IDataProtector` traz um anel de chaves próprio, outro segredo para gerir, quando o HMAC com chave de configuração já atende.
- Um segundo índice ascendente por conta e instante. O existente serve na ordem inversa, e outro índice na maior tabela custa escrita.
- Tornar `to` inclusivo. Muda um contrato fixado nos requisitos.

## Consequências

A paginação custa o mesmo na primeira página e na milésima, e a página não muda quando entram lançamentos novos. Trocar a chave invalida os cursores em voo e os clientes recomeçam a paginação, custo aceito porque um cursor só vale durante uma paginação, e a versão do formato é o gancho para uma rotação com duas chaves ativas. Quem reconstrói saldo pelo extrato precisa conhecer o microssegundo: o saldo de abertura de uma janela é o `balanceAfter` do item mais antigo menos o valor com sinal dele. Se uma rotação de chave não puder invalidar cursores, ou se um perfil de leitura fizer o plano deixar de usar o índice, o desenho precisa ser revisto.
