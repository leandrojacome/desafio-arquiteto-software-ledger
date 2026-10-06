# Documento de arquitetura 0017: Retenção de 35 dias das chaves de idempotência

## Contexto

O [documento de arquitetura 0006](0006-idempotencia-por-chave-e-hash.md) precisa de um prazo para as chaves, e há três respostas possíveis: não expirar nunca, sete dias ou algo perto de 35. A conta está em [Capacidade e escala](../../08-resiliencia-e-operacao/capacidade-e-escala.md): com a média premissada de 200 lançamentos por segundo e uns 150 bytes por chave, sem expiração são 6,3 bilhões de linhas por ano, perto de 950 GB por ano e 9,5 TB em dez anos. O disco é o problema menor. O índice `(account_id, idempotency_key)` tem chaves aleatórias, então cada escrita visita uma página qualquer dele. Enquanto o índice cabe na memória, a visita é barata. Quando deixa de caber, vira leitura aleatória de disco dentro de uma transação que tem 150 ms de p99 para fechar, em todo lançamento e não só nos raros reenvios.

O argumento a favor de não expirar é real: uma chave podada não volta, e um reenvio tardio cria um segundo lançamento. Só que ele depende do prazo máximo de reenvio dos chamadores, e quem o conhece são os donos dos sistemas de Pix e de cartões. Nenhum o confirmou.

## Decisão

As chaves são guardadas por 35 dias, contados de `created_at`. O Worker remove as vencidas em lotes, por `DELETE` limitado e ordenado, e a mesma passagem poda `account_creation_keys` ([documento de arquitetura 0035](0035-idempotency-key-opcional-na-criacao-de-conta.md)). Passados os 35 dias, a mesma chave é um pedido novo, e isso é contrato com os chamadores: a janela de nova tentativa e de reprocessamento deles tem de caber em 35 dias ([Contrato da API REST](../../05-contratos/api-rest.md)). Trinta e cinco dias cobrem um reprocessamento mensal de arquivo com folga de alguns dias, o caso mais longo que consigo conceber sem ouvir os donos.

A poda precisa de duas coisas no esquema, ambas na migração `0004`: o privilégio `DELETE` em `idempotency_keys` para o papel do Worker e o índice `ix_idempotency_keys_created_at`, para o lote não varrer a tabela. O índice é comum e não BRIN, porque as páginas liberadas pela poda são reaproveitadas por inserções novas e a correlação física com `created_at` se perde. Se a leitura da repetição não achar a linha, a chave foi podada entre o conflito e a leitura, e a tentativa recomeça como pedido novo, o mesmo comportamento de quem repete depois dos 35 dias.

## Alternativas descartadas

- Sem expiração. É a mais segura contra reenvio tardio e a mais cara: 9,5 TB só de chaves e um índice que sai da memória em poucos meses.
- Sete dias. O índice ficaria em uns 8 GB, mas só se justifica se os donos de Pix e de cartões garantirem por escrito que nada é reenviado depois disso. Mudar o prazo é só configuração.
- Vinte e quatro horas, como em várias APIs públicas. Curto demais para um banco que reprocessa arquivos de liquidação.
- Unicidade direto em `ledger_entries`. Põe num índice de dez anos um dado que só importa por semanas.
- Tabela fria para as chaves antigas. Preserva a proteção sem pagar o índice, ao custo de uma segunda consulta e de uma rotina de arquivamento que nenhum requisito pede.

## Consequências

Em regime são 605 milhões de linhas e cerca de 90 GB, dos quais uns 40 GB são do índice da chave primária, o que cabe na memória de um servidor de 128 GB. São estimativas sobre dado sintético, e o volume real ainda não foi medido ([Limites conhecidos](../../09-qualidade/limites-conhecidos.md)).

O risco novo é a duplicação por reenvio depois de 35 dias. Ele não desaparece, só é empurrado para um prazo que o chamador controla, e reduzir os 35 dias depende da resposta dos donos de Pix e de cartões. A poda é uma das rotinas descritas em [Fluxo: rotinas de manutenção do Worker](../../06-fluxos/rotinas-de-manutencao.md).
