# Como contribuir

Obrigado por ajudar a manter o TS SE Tool Community.

## Antes de relatar um erro

1. Reproduza o problema em uma cópia de um save manual.
2. Feche o jogo antes de editar.
3. Faça apenas uma alteração isolada.
4. Recarregue o save no editor.
5. Anexe `log.log`, `errorlog.log` e `compatibility.log`.
6. Informe a versão do jogo, formato do save, DLCs/mods ativos e passos exatos.
7. Não publique um save pessoal. Use uma fixture mínima anonimizada ou ofereça o arquivo de forma privada ao mantenedor.

## Pull requests

- Preserve os cabeçalhos originais de copyright e licença.
- Mantenha a alteração cirúrgica e limitada a um único domínio.
- Nunca ignore a validação do writer moderno nem reative o serializador legado para saves modernos.
- Adicione marcadores de log para inicialização, preparação e validação.
- Inclua evidência de diff antes/depois, recarga e validação no jogo.
- Compile em `Release|Any CPU` com .NET Framework 4.7.2.

## Propostas não aceitas

Não serão incorporadas alterações que habilitem inserção/exclusão bruta de blocos, caminhos manuais de acessórios sem verificação ou mutações do grafo sem validação completa das referências.
