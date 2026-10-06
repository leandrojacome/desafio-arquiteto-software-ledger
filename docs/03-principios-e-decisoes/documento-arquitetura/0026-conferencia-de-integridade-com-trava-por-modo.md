# Documento de arquitetura 0026: Conferência de integridade, com uma trava por modo

## Contexto

O ledger não corrige nem apaga, então a pergunta que sobra é se o que está gravado ainda fecha. O saldo guardado deve ser o `balance_after` do último lançamento, cada `balance_after` deve ser o anterior mais o valor com sinal, e a posição da conta não pode ter lacuna. A conferência roda no Worker, com uma única instância ativa por vez. Três coisas pediam decisão: o que ela prova, como duas cadências convivem e de onde sai o ponto de partida de cada execução.

## Decisão

A conferência faz duas consultas: o topo (saldo guardado contra o último lançamento e contra o piso) e a cadeia (cada lançamento contra o anterior). Por indução na posição, as duas provam que o saldo é a soma dos lançamentos sem ler o histórico inteiro toda noite. A soma direta existe como procedimento sob demanda, o comando `--inspect-account` do Worker.

Há dois modos. O recente roda a cada 5 minutos sobre a janela que começa um minuto antes do fim da execução anterior, porque uma transação em voo pode confirmar depois. O completo roda a cada 24 horas sobre tudo. A cadeia de uma janela grande é fatiada em consultas de até 10 minutos, para caber no `statement_timeout` de 10 segundos da fonte do Worker. Cada modo tem a sua trava consultiva, `pg_try_advisory_lock(727002, modo)`: a execução abre uma conexão, toma a trava e roda todas as consultas nela. Quem não consegue a trava pula a execução, e a morte da conexão a libera sozinha. O ponto de partida vem do último `integrity.run_completed` do modo em `audit_log`, sem tabela de controle, e para isso a migração `0003` dá ao Worker `SELECT` por coluna nessa tabela, limitado a cinco colunas.

A conferência nunca corrige e não bloqueia a conta. O log de erro leva conta, lançamento, verificação e identificador da execução, e os valores monetários ficam só na trilha de auditoria, que tem acesso restrito e retenção longa. O passo a passo está em [Fluxo: conferência de integridade](../../06-fluxos/conferencia-de-integridade.md).

## Alternativas descartadas

- Uma trava para os dois modos. A execução completa, que a 20 milhões de contas deve levar de 8 a 15 minutos pela estimativa de dimensionamento, seguraria a recente durante toda a completa, e o alerta de conferência atrasada dispararia sem defeito nenhum.
- Somar o histórico na execução completa. Lê a tabela inteira toda noite pelo mesmo resultado que a indução dá.
- Guardar o ponto de partida na memória do Worker. Um reinício perderia a janela, e uma tabela de controle seria outra migração para a mesma informação.
- Uma conexão para a trava e outra para as consultas. Com os dois modos juntos seriam quatro conexões num pool de cinco, margem curta demais para o publicador do outbox.
- Corrigir o que a conferência encontra, ou travar a conta. Contraria a imutabilidade e a regra de que o ledger avisa e deixa um humano decidir.
- Valor monetário no log de erro. O nível `Error` está acima de `Information`, e a regra proíbe valor monetário nesse nível.

## Consequências

Os dois modos rodam juntos sem se atrapalhar, e o Worker reiniciado retoma do ponto certo. A conferência não pega uma reescrita consistente de um `balance_after` antigo e de todos os seguintes, porque a cadeia continua fechando. Esse buraco é de quem tem privilégio de dono e desliga o gatilho, e quem o cobre é a imutabilidade em três camadas ([documento de arquitetura 0002](0002-ledger-imutavel-somente-insercao.md)) e a auditoria do banco. As durações são estimativas: a execução completa não foi medida em escala, e o que falta exercitar está em [Limites conhecidos](../../09-qualidade/limites-conhecidos.md). Uma execução completa que passe de 26 horas (as 24 da cadência mais 2 de folga do alerta `LedgerIntegrityCheckStale`) dispararia o alerta com tudo funcionando, e o desenho do modo completo teria de mudar.
