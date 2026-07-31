# Compatibilidade

## Euro Truck Simulator 2

- Alvo publicamente suportado pela versão `0.3.11.47`.
- Validação comunitária principal realizada em save moderno de formato `97`.
- A camada existente reconhece formatos de save `61–97`, mas a evidência mais forte é a fixture moderna efetivamente testada.
- Atualizações do jogo podem alterar campos e grafos de referência. Consulte as Issues antes de editar após uma atualização importante.

## American Truck Simulator

O ATS é **experimental e não validado** nesta versão, pois o mantenedor não possui um ambiente de teste do jogo. Isso não bloqueia a distribuição destinada ao ETS2.

Relatos voluntários de ATS devem:

- usar uma cópia do save;
- informar versão do ATS e formato do save;
- realizar uma única alteração isolada;
- anexar os três arquivos de log;
- confirmar recarga no editor e funcionamento no jogo;
- fornecer, quando possível, um diff anonimizado antes/depois.

Nenhuma declaração de suporte ao ATS deve ser feita até que os testes críticos sejam concluídos.

## Atualização automática

O verificador de versão consulta `latest.txt`. O download automático herdado não faz parte da homologação H21. Utilize a página **Releases** e confira `SHA256SUMS.txt` antes de executar o arquivo.
