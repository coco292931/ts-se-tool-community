# Validação da RC5.1H22

## Escopo

- Produto: TS SE Tool Community `0.3.11.48 — RC5.1H22 Final`.
- Base funcional preservada: `0.3.11.47 — RC5.1H21 Final`.
- Jogo validado: **Euro Truck Simulator 2 1.60**.
- Formato de save validado: **97**.
- ATS: experimental e não validado.

## Alterações da H22

- correção do endereço usado pelo atualizador automático;
- asset estável `TS.SE.Tool.latest.zip` na release mais recente;
- validação SHA-512 preservada antes da atualização;
- compatibilidade exibida no programa sem ampliar a homologação para saves antigos;
- versão incrementada para `0.3.11.48`;
- documentação, página pública, release e formulários sincronizados.

## Limites preservados

Contratar/demitir motoristas, edição bruta do grafo, colagem completa de veículos, reordenação estrutural e caminhos de acessórios não verificados continuam bloqueados.
## Resultado da validação em ambiente real

Validação concluída em **31/07/2026**, no Windows e no **Euro Truck Simulator 2 1.60**:

- executável `0.3.11.48` inicializado normalmente;
- RC5.1H22 identificada no log;
- save moderno de formato `97` carregado;
- alteração controlada gravada com sucesso;
- save recarregado pelo editor;
- save aberto normalmente no ETS2 1.60;
- nenhum erro real encontrado no log operacional;
- diagnósticos modernos mantidos separadamente em `compatibility.log`.
