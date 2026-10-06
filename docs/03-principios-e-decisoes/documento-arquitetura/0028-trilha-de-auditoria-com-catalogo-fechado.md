# Documento de arquitetura 0028: Trilha de auditoria com catálogo fechado e teto na negação

## Contexto

O modelo de segurança pede uma trilha do que importa para quem investiga: quem criou uma conta, quem leu um documento em claro, quem tentou escrever sem poder. Duas tensões atravessam o desenho. Gravar toda negação de escrita contradiz a regra de não transformar ataque em carga de escrita: um token de leitura válido gera um 403 a cada tentativa, e o limitador por chamador deixaria passar até 1.500 por segundo, ou seja, 1.500 inserções por segundo no banco do ledger. E o campo livre de detalhes é o caminho mais curto para um dado pessoal entrar na trilha. O lançamento aceito já é a sua própria trilha, porque a linha de `ledger_entries` carrega `client_id`, `correlation_id`, instante do banco, conta e tipo.

## Decisão

A trilha é a tabela `audit_log`, somente de inserção como as outras (gatilho mais privilégio). Os eventos saem de um catálogo fechado: `account.created`, `authorization.denied_write`, `pii.decrypted`, `pii.rewrapped`, `keys.version_activated`, `integrity.run_completed` e `integrity.violation_detected`. O `AuditEvent` só se constrói pelas fábricas de `AuditEvents`, que montam os `details` por `Utf8JsonWriter` a partir de valores tipados. Não existe dicionário livre de detalhes, e a rota da negação é sempre o molde da rota, nunca o caminho com identificadores.

Há duas formas de gravar. A criação de conta grava a linha na mesma transação da conta e do saldo: existem os três ou nenhum. A negação de escrita é gravada fora de qualquer transação, em segundo plano: a resposta não espera, a exceção é capturada, e antes de gravar o pedido passa por um balde por chamador de capacidade 10 e reposição de 1 por segundo. O excedente não é gravado e vai para a métrica `ledger.audit.skipped`, com o motivo `rate_capped`, e para o log.

Não entram na trilha o 401, que não identifica ninguém, a recusa por regra de negócio, como saldo insuficiente, o lançamento aceito e a consulta de saldo ou extrato, que tem evento de log em categoria própria. Retenção e acesso estão em [Trilha de auditoria](../../07-consistencia-e-seguranca/trilha-de-auditoria.md).

## Alternativas descartadas

- Gravar toda negação, sem teto. Ataque vira carga de escrita no banco que guarda o dinheiro.
- Não gravar a negação. O rastro de quem tentou o quê é exatamente o que a trilha existe para dar.
- Gravar a negação de forma síncrona. O 403 passaria a depender do banco, e uma falha na trilha viraria falha para o chamador.
- `details` como objeto livre. Basta um descuido para um dado pessoal entrar na trilha, e com o catálogo fechado esse descuido não compila.
- Uma tabela por tipo de evento. O volume é baixo, e uma tabela só mantém imutabilidade e consulta num lugar.

## Consequências

O rastro de uma tentativa isolada fica completo e o de uma rajada fica amostrado. Quem precisa de todas as negações não as encontra aqui, e se a conformidade exigir isso o caminho é um armazenamento externo e imutável, não mais linhas no banco do ledger. Uma falha ao gravar a negação (banco fora) não muda o 403, vira log e métrica. Duas instâncias do Worker subindo ao mesmo tempo podem gravar duas linhas de `keys.version_activated`, o que se aceita numa trilha que só registra fatos. O papel do Worker lê só cinco colunas da tabela, as que a conferência de integridade precisa ([documento de arquitetura 0026](0026-conferencia-de-integridade-com-trava-por-modo.md)).
