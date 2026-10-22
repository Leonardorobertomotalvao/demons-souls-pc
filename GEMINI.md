# Orientacoes para agentes - Demon's Souls recompilado para Windows

Objetivo: melhorar o port nativo de Demon's Souls com mudancas pequenas, testadas e reversiveis.

- Comece lendo README.md e recovery/CONTINUAR-DESENVOLVIMENTO.md, se existirem.
- Inspecione o estado real do codigo e dos relatórios; nao suponha que um problema ja esteja resolvido.
- Trabalhe em UM problema tecnico por vez. Explique o plano antes de mudancas grandes.
- Nao modifique nem apague releases/Alpha-0.1, backups, saves, PS3_GAME, work/game/EBOOT.ELF, discos montados ou arquivos originais do jogo.
- Nao envie, publique ou inclua arquivos proprietarios do jogo em commits, issues ou respostas externas.
- Pecas de codigo, nomes de arquivo e logs devem ser conferidos antes de referenciar qualquer resultado como validado.
- Antes de alterar codigo, verifique git status e preserve alteracoes preexistentes. Nao use git reset --hard ou git clean -fd.
- Se precisar compilar, confirme primeiro os alvos e locais de saida; prefira builds separados e nao altere os executaveis da Alpha.
- Execute apenas testes relevantes. Registre comando, saida, limites da validacao e os proximos passos.
- Nao faca alteracoes em lote, force-push, publicacao, instalacao de dependencias ou exclusao de dados sem permissao explicita.
- Nao tente contornar cotas de modelos; se um servico atingir limites, documente o ponto de parada para outro agente continuar.