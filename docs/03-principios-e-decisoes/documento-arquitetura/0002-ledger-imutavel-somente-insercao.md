# Documento de arquitetura 0002: Ledger imutável, somente inserção

## Contexto

O ledger é a prova do que aconteceu. Auditoria e conciliação precisam reconstruir o saldo de qualquer conta em qualquer instante dos últimos 10 anos ([Restrições e premissas](../../02-contexto-e-requisitos/restricoes-e-premissas.md)), e o que pode ser alterado vira suspeito. Se um lançamento pudesse ser editado, o `balance_after` guardado em cada linha ([documento de arquitetura 0003](0003-saldo-apos-em-cada-lancamento.md)) também deixaria de merecer confiança.

## Decisão

`ledger_entries` aceita só `INSERT` e `SELECT`. Não há `UPDATE`, `DELETE` nem exclusão lógica. Erro se corrige com estorno: um lançamento novo, de tipo oposto e mesmo valor, com `reverses_entry_id` apontando para o original, que fica intacto.

A regra tem três camadas, porque convenção sozinha não segura uma correção feita às pressas num incidente. No código, o `LedgerEntriesAreNeverMutatedTests` varre as constantes SQL de produção e reprova qualquer instrução que altere `ledger_entries` ou `audit_log`. Nas permissões, o papel da API só tem `SELECT` e `INSERT` e o do Worker só `SELECT`. No banco, gatilhos barram `UPDATE`, `DELETE` e `TRUNCATE` de quem entrar com um papel mais forte por engano. Um superusuário pode desligá-los, então são rede de proteção, não controle de segurança.

O estorno é sempre total. Um índice único parcial garante no máximo um por lançamento, mesmo com requisições concorrentes, e o estorno passa pela mesma regra de saldo de qualquer lançamento: estornar um crédito já gasto é recusado se a conta ficasse abaixo do limite. Abrir exceção criaria saldo negativo fora de qualquer limite de crédito da conta ([Questões em aberto](../questoes-em-aberto.md)).

## Alternativas descartadas

- Tabela mutável com histórico alimentado por gatilho. São duas fontes de verdade, e "corrigir direto no banco" vira gesto rotineiro.
- Exclusão lógica com flag de cancelamento. É um `UPDATE` com outro nome e invalida o `balance_after` dos lançamentos seguintes.
- Event sourcing completo. A metade útil já existe, porque uma tabela só de inserção é um log. O resto é custo sem demanda.
- Cadeia de hashes entre lançamentos. A conferência de integridade já recalcula a cadeia de saldos, e o hash entra se a auditoria exigir prova criptográfica.

## Consequências

Auditar é fazer uma consulta, e a retenção de 10 anos sem remoção física vira propriedade do sistema. O lançamento carrega só o identificador da conta, e o documento do titular fica cifrado em `accounts` ([documento de arquitetura 0010](0010-seguranca-jwt-e-criptografia-de-pii.md)), então não há dado pessoal a apagar dentro do ledger.

O lado ruim: erro humano é permanente, e o extrato mostra o lançamento errado e o estorno. A tabela só cresce, e sem particionamento ([Evolução futura](../../11-evolucao/evolucao-futura.md)) índice e backup crescem junto. E sem `UPDATE` não há tuplas mortas para disparar o autovacuum, então a migração `0001` fixa `autovacuum_vacuum_insert_scale_factor` em 0,01, ou as buscas index-only do saldo passado viram leitura de heap.
