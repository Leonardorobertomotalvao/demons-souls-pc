Demon's Souls PC — Launcher {VERSION} (Windows x64)
Este texto acompanha o pacote {TAG}. Ele não contém o jogo.

PT — O QUE ESTE PACOTE CONTÉM
- Launcher compilado (self-contained .NET 8; não é preciso instalar o .NET).
- NÃO contém o jogo: nem des_boot.exe, nem EBOOT.ELF/EBOOT.BIN, firmware, chaves,
  dados protegidos ou saves. Você fornece esses arquivos a partir da sua cópia legal.
- README.md completo do projeto e docs/03-setup.md dentro das pastas do pacote.

PT — COMO BAIXAR E RODAR
1. Extraia o ZIP inteiro em uma pasta gravável, mantendo a pasta Launcher\ junta.
2. Execute Launcher\DemonsSouls.Launcher.exe (o mesmo exe do hash abaixo).
3. Você deverá fornecer:
   - des_boot.exe — um runtime aprovado (compilado por você ou fornecido pelo
     projeto em outra distribuição; o pacote do launcher não o traz);
   - EBOOT.ELF — o da sua cópia do jogo decriptada (ex.: via RPCS3).
4. Use PREPARAR (importar a sua cópia PS3_GAME para a instalação gerenciada) ou
   REPARAR; em Avançado, confirme os caminhos do runtime e do EBOOT; escolha
   60 FPS e clique JOGAR.

PT — HASHES (para conferência)
| Arquivo | SHA-256 |
|---|---|
| Pacote .zip publicado | ver `DemonSoulsPC-Launcher-Windows-x64-SHA256.txt` anexado à Release |
| Launcher.exe dentro do pacote | {EXESHA} |
| Fonte compilada (commit) | {COMMIT} |

PT — ATUALIZAÇÃO A PARTIR DE UMA VERSÃO ANTERIOR
- Substitua o exe na pasta Launcher\; a instalação gerenciada existente continua
  válida (o launcher revalida por identidade NTFS/USN; nenhuma migração é exigida).
- Novidade desta versão (detalhes no CHANGELOG.md do repositório e
  docs/35-launcher-prepare-optimization.md): preparação/verificação otimizadas — a
  instalação íntegra NÃO é relida (4,0 s de `--verify` sobre 8,77 GiB na máquina de
  referência); reparo de um arquivo cai de 413 s para 16,1 s; re-preparação com a
  mesma ISO: 9,1 s com zero leituras (identidade assinada); validadores corrigidos
  para aceitar artefatos runtime legítimos (`.arealoadlist`, buckets vazios).

PT — O QUE FOI TESTADO (e o que NÃO)
- Testado nesta release: build Release (0 avisos/0 erros), self-test completo
  (SELF_TEST_PASS), benchmarks A1/A1b/A2/A3 + S1/S2/S3 na instalação real de
  8,77 GiB, iniciação por CLI em pasta isolada, conferência de conteúdo do ZIP.
- Testado antes NESTA máquina de referência (documentado no repo): jogo rodando até
  Boletaria a 60 FPS; correção de áudio validada por A/B e auditivamente.
- NÃO testado: o resto do jogo, outras GPUs/placas, máquinas diferentes da do
  autor. A release NÃO comprova o jogo inteiro.

PT — LIMITAÇÕES
- Experimental e não oficial — sem afiliação com Sony Interactive Entertainment,
  FromSoftware ou Atlus. Sem online. Sem garantias. Mantenha backup dos saves.

EN — WHAT THIS PACKAGE CONTAINS
- Compiled launcher (self-contained .NET 8; no .NET installation required).
- NO game content: no des_boot.exe, no EBOOT.ELF/EBOOT.BIN, firmware, keys,
  protected data, or saves. You supply these from your legally obtained copy.
- Project README.md and docs/03-setup.md inside the package folders.

EN — HOW TO DOWNLOAD AND RUN
1. Extract the WHOLE zip into a writable folder, keeping the Launcher\ folder together.
2. Run Launcher\DemonsSouls.Launcher.exe (exe hash below).
3. You must supply: an approved des_boot.exe (compiled by you or provided in
   another distribution; this package does not include it) and the EBOOT.ELF
   from your decrypted game copy (e.g. via RPCS3).
4. Use PREPARE (imports your PS3_GAME copy into the managed installation) or
   REPAIR; under Advanced, confirm the runtime and EBOOT paths; pick 60 FPS
   and click PLAY.

EN — HASHES
| File | SHA-256 |
|---|---|
| Published .zip | see `DemonSoulsPC-Launcher-Windows-x64-SHA256.txt` attached to the Release |
| Launcher.exe inside the package | {EXESHA} |
| Compiled source (commit) | {COMMIT} |

EN — UPDATING FROM A PREVIOUS VERSION
- Replace the exe inside Launcher\; your existing managed installation remains
  valid (revalidated by NTFS/USN identity; no migration required).
- What's new in this version (see CHANGELOG.md and docs/35 in the repository):
  optimized prepare/verify — an intact installation is NOT re-read (4.0 s
  `--verify` over 8.77 GiB on the reference machine); single-file repair drops
  from 413 s to 16.1 s; re-prepare from the same ISO: 9.1 s with zero reads
  (signed identity); validators accept legitimate runtime artifacts now.

EN — WHAT WAS TESTED (and what was NOT)
- Tested on this release: Release build (0 warnings/0 errors), full self-test
  (SELF_TEST_PASS), benchmarks A1/A1b/A2/A3 + S1/S2/S3 on the real 8.77 GiB
  installation, isolated-directory CLI startup, ZIP content audit.
- Previously tested on THIS reference machine (documented in the repo): the game
  running to Boletaria at 60 FPS; the audio fix validated by A/B and audibly.
- NOT tested: the rest of the game, other GPUs/devices, machines other than the
  author's. This release does NOT prove the whole game works.

EN — LIMITATIONS
- Experimental and unofficial — no affiliation with Sony Interactive Entertainment,
  FromSoftware, or Atlus. No online. No guarantees. Keep your save backups.
