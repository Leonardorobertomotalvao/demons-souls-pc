# Demon's Souls PC — recompilação estática nativa para Windows x64

**Porte experimental "Demon's Souls PC" (Alpha 0.2).** Tradução estática (*static recompilation*) de *Demon's Souls* (PS3, América do Norte, BLUS30443) para Windows x64: o código PowerPC/SPU do jogo é convertido para C antecipadamente (AOT), compilado e ligado a uma reimplementação (clean-room) das bibliotecas de sistema do PS3. Renderização via Direct3D 12, áudio via WASAPI, launcher em WPF/.NET 8 para instalação, verificação de integridade e inicialização.

> **Este repositório não contém código do jogo, dados do jogo, saves, firmware, BIOS, chaves ou executáveis do jogo.** Você precisa da sua própria cópia legal. Veja [Licenciamento e avisos](#17-licenciamento-e-avisos).

---

## 1. O que é este projeto

Um fan port pela técnica de recompilação estática, no modelo de *Zelda 64 Recompiled* e *Unleashed Recompiled*: em vez de emular o console em tempo real, o executável do jogo (decriptado localmente pelo usuário) é traduzido uma única vez para código nativo x64, resultando em `des_boot.exe`, um executável Windows que roda o jogo em até 60 FPS com baixa latência. As bibliotecas do PS3 (cellAudio, cellAtrac, camera, input, SPURS/lv2 etc.) são reimplementadas à mão. O launcher Windows prepara a cópia do jogo dentro de uma instalação gerenciada, valida identidades SHA-256 do runtime e do EBOOT e inicia o jogo — sem emulação general-purpose em tempo de execução.

Construído sobre [ps3recomp](https://github.com/sp00nznet/ps3recomp) (MIT) e o fork [JonathanDC64/ps3recomp](https://github.com/JonathanDC64/ps3recomp); as contribuições deste projeto são os patches em `patches/` e a camada de porte em `port/`.

## 2. Objetivo

1. Preservar *Demon's Souls* em PC nativo, sem depender de emulação em tempo real.
2. Rodar o jogo a 60 FPS (e além, em regime experimental) respeitando a mecânica original de 30 FPS.
3. Corrigir defeitos conhecidos — áudio, controles, menu — com mudanças pequenas, testadas e reversíveis.
4. Manter o projeto estritamente legal: nenhum material derivado do jogo é publicado; o usuário fornece a própria cópia.

## 3. Estado atual (2026-10-22) — Alpha 0.2, **experimental**

Nível de maturidade: **protótipo funcional, validado em uma máquina de referência única (Windows 10, computador do autor).** Nada abaixo pode ser assumido para outras máquinas.

### 3.1 Comprovadamente testado (máquina de referência)

- **Inicialização completa até Boletaria**: logos, auto-save, abertura com som e legendas, tela-título, criação de personagem, tutorial, Nexus e Palácio Boletariano 1-1.
- **Renderização D3D12**: cena iluminada, sombras, HUD, partículas, *light shafts* e haze; shaders pré-compilados (o jogo não pausa para compilar shader).
- **Save/load funcionando**, incluindo sons de menu.
- **60 FPS**: perfil aprovado por verificação de identidade do runtime; em 1-1, com `DES_FPS=60` e `VBLANK_MHZ=240000`, chegaram a medir ~120 FPS **nessa máquina específica** — não é promessa para outras configurações.
- **Correção do bug de silêncio total de áudio na inicialização**: causa raiz documentada (corrida da fila de eventos do libmixer) e correção publicada em `patches/des-audio-notify-fix.patch`; validada automaticamente por A/B (o PCM chega ao WASAPI com o fix ligado e não chega desligado; 3752/3752 e 8442/8442 notificações em dois conjuntos de testes; detalhes em `docs/34`) **e auditivamente pelo autor em 2026-10-22, numa instalação isolada** (runtime `83B238…`).
- **Launcher Alpha 0.2**: compila, abre, importa/repara instalações, aplica gates de identidade (bloqueia runtime não aprovado) e registra cada operação em trace JSONL.

### 3.2 Passou em teste técnico; falta validação em outras máquinas

- Compilação/execução do launcher e runtime em máquinas de build diferentes da do autor.
- Áudio em outros dispositivos/saídas (o teste auditivo cobre o setup WASAPI do autor apenas).
- Controles além de teclado+mouse e DualSense (USB/Bluetooth-HID): não testados.
- FPS em outras GPUs/drivers.

### 3.3 Experimental / não aprovado

- Desempenho total, em qualquer máquina, prometido. **Não há garantias.**
- Jogo além da área inicial (resto do jogo não testado).
- Online/rede: não implementado.
- LLE completo de SPURS/SPU: abandonado após experimento (`docs/22`); runtime usa HLE + imagens SPU recompiladas.

## 4. Recursos implementados e em desenvolvimento

**Implementado** (ver `docs/28-in-game.md` para o inventário mais preciso): boot PPU completo, pipeline de imagens SPU recompiladas (LIBSRE), SPURS HLE com coordenação de tarefas, cellAudio/cellAtrac/cellFs/camera/input, D3D12 com fila RSX e predição PSO, UI overlay própria (RmlUi) para opções/menùs, launcher com import/repair/gates/tracing.

**Em desenvolvimento**: transparências que desaparecem em frames únicos (muro-fog, aura de alma, neve), gerenciador de cutscenes > 30 Hz, física > 60 FPS, mais controles, config persistente do usuário.

## 5. Arquitetura

```
(uma vez, pelo usuário)                     (reprocomp)
EBOOT.BIN do jogo (SELF) ──RPCS3/scetool──▶ EBOOT.ELF decriptado (PowerPC, big-endian)
                                                   │
                                                   ▼
                      ps3recomp upstream + patches/des-audio-notify-fix.patch
                      (PPU/SPU → C AOT, extração de imagens SPU, importação de NIDs)
                                                   │
                                                   ▼
                              runtime nativo des_boot.exe (x64)
                      libs PS3 reimplementadas + port/ (UI, heap, menu SFX, stubs)
                                                   ▲
                                                   │ lançamento validado (SHA-256)
                              launcher DemonsSouls.Launcher.exe (WPF/.NET 8)
                      PREPARAR/REPARAR → vfs/PS3_GAME + runtime/EBOOT.ELF + EXE
                      installation.json (manifesto SHA) + OperationTrace JSONL
```

- **Só código próprio + tooling** entra no repo. `ps3recomp` não é distribuído aqui; o patch de áudio se aplica no fork upstream no commit `add3cac07e29b4457ab24303804bfcc96056ba5a`.
- **port/**: camada do porte — CMake/`EmbedFiles`, UI RmlUi diretamente no jogo (fontes OFL/Apache incluídas com licenças), heap, menu-sfx, opÃ§Ãµes, stubs.
- **launcher-alpha02-hotfix/**: launcher completo — `Installation.cs` (import, manifesto), `RuntimeService.cs` (gates/60 FPS), `OperationTrace.cs`, `VfsPolicy.cs`/`VfsPolicyTest.cs`, `SelfTest.cs`, `NativeStart.cs`, `Program.cs` (CLI reprodutível).
- **Integração launcher↔runtime**: diretório de instalação gerenciada com `installation.json`; perfil de dados `%LOCALAPPDATA%\DemonsSoulsPCLauncher` ou `DEMONS_SOULS_LAUNCHER_HOME` para home isolada; variáveis `DES_*` são limpas na inicialização do runtime.

## 6. Estrutura de pastas

| Pasta | Função |
|---|---|
| `docs/` | Documentação técnica crônica (00–34): setup, análise, boot, SPURS/SPU, legal, launcher, áudio |
| `launcher-alpha02-hotfix/` | Fonte do launcher Windows atual (WPF/.NET 8, self-contained) |
| `launcher/` | Fonte do launcher original (legado; o atual é o `hotfix`) |
| `port/` | Camada do porte: CMake, UI RmlUi (fontes OFL/Apache com licenças), heap, menu SFX, options, stubs |
| `patches/` | Patch do fix de áudio sobre o `ps3recomp` upstream |
| `scripts/` | Build/análise/diagnóstico: `build_libsre.sh`, `build_spu.py`, `apply_boot_patches.py`, `audio-ab-diagnostic.ps1`, `vsenv.bat` etc. |
| `tools/` | Ferramentas de análise/depuração (Ghidra, profilers, shaders, SPU trace) |

Diretórios locais e reaproveitados fora do Git (nunca publicados): `work/` (arquivos de jogo + C transliterado), `releases/`, `backups/`, `cache/`, `build-logs/`, `recovery/`, `PS3_GAME/`.

## 7. Requisitos

### Para jogar (pacote do launcher pré-compilado)
- Windows 10/11 x64; GPU com D3D12 FL 12.0+.
- Pacote do launcher self-contained (não precisa instalar .NET).
- Sua cópia legal do jogo (PS3 USA) devidamente decriptada — o launcher exige `EBOOT.ELF` e `des_boot.exe` com identidades aprovadas.
- Espaço em disco para a instalação gerenciada (cópia do jogo + vfs + perfil).

### Para desenvolver
- Windows 10/11 x64; VS 2022 BuildTools (C++ + Win SDK com `d3d12.h`), CMake ≥ 3.20, Ninja, clang conforme o fork.
- Python 3.11+ com `ps3recomp/requirements.txt` (pycryptodome, capstone, construct, tabulate, tqdm) para o tooling de análise.
- .NET SDK 8 (launcher).
- RPCS3 (ou scetool com chaves públicas lv0/metldr) para decriptar **a sua própria cópia** — o projeto não distribui decriptação nem chaves.
- Ghidra opcional (análise).

## 8. Preparação legal dos arquivos — essência

O usuário fornece: (1) cópia legal do jogo; (2) `EBOOT.ELF` **decriptado** — via recomendada: RPCS3 com a sua cópia, ativando o dump do executável; alternativa: `scetool` com chaves públicas sobre a sua cópia. A decriptação é feita **localmente, na sua máquina, para interoperabilidade**; chaves, dumps e assets nunca são comitados, publicados ou compartilhados. Sua pasta `PS3_GAME` (USRDIR, PARAM.SFO, EBOOT.BIN original) nunca sai do seu computador; o launcher só copia internamente para a instalação gerenciada.

## 9. Compilando passo a passo

### 9.1 Launcher
```powershell
git clone <este repo> demons-souls-pc && cd demons-souls-pc
dotnet publish launcher-alpha02-hotfix\DemonsSouls.Launcher.csproj -c Release -r win-x64 --self-contained true -o ..\launcher-build
# → ..\launcher-build\DemonsSouls.Launcher.exe (inclui gate de identidade e CLI)
```

### 9.2 Runtime (ps3recomp + patch + port)
```
1. git clone https://github.com/JonathanDC64/ps3recomp
   git -C ps3recomp checkout add3cac07e29b4457ab24303804bfcc96056ba5a
   git -C ps3recomp apply <repo>\patches\des-audio-notify-fix.patch
2. Configure o ambiente MSVC (vcvars64) — scripts/vsenv.bat
3. Coloque seu jogo decriptado em work\game\ (conforme docs/03-setup.md)
4. Configure/compile os alvos CMake (port + runtime) para x64
5. Resultado: port\build\des_boot.exe
```

**Honestidade sobre reprodutibilidade**: o launcher tem build única reproduzível (9.1). O runtime depende do fork upstream, da sua cópia legal decriptada localmente e do toolchain de análise documentado em `docs/03-setup.md`, `docs/06`, `docs/19` — **não há "um botão" build-all**. Builds em máquinas diferentes podem divergir em bytes do binário do autor; o launcher aprova por identidade SHA-256 (para rodar com a SUA build, reproduza os próprios gates da sua instalação).

### 9.3 Pacote do launcher (zip) — `scripts/make_launcher_release.ps1`
```powershell
# package base (sem runtime): release\DemonSoulsPC-Launcher-Windows-x64-<versão>.zip
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\make_launcher_release.ps1
# opcional: incluir o runtime aprovado SOMENTE se o SHA-256 conferir;
# o arquivo indicado nunca é comitado no repositório
powershell ... -File scripts\make_launcher_release.ps1 -ApprovedRuntime "E:\caminho\des_boot.exe"
```
O zip traz `Launcher\` self-contained, `README.md` e `docs/03-setup.md`. Nenhum material derivado do jogo entra no pacote.

## 10. Executando o jogo pelo launcher

1. Abra `DemonsSouls.Launcher.exe`.
2. **SELECIONAR ISO LOCAL** — o launcher monta a imagem em modo somente leitura (se o Windows já a tiver montada, apenas usa a unidade), identifica a edição pelo `PARAM.SFO` (BLUS30443 · 01.00) e preenche a pasta de origem automaticamente. Se o Windows recusar a montagem, faça duplo clique na ISO no Explorador e repita.
3. O **assistente de preparação** (aba Instalação) mostra, em tempo real, cada requisito: imagem/origem, `EBOOT.ELF` preparado, executável nativo aprovado, destino e espaço. Ele localiza candidatos válidos junto à imagem montada, no destino ou lembrados nas configurações e confere as identidades SHA-256 antes de liberar JOGAR.
4. Sobre o `EBOOT.ELF`: o launcher **não descriptografa** a `EBOOT.BIN` protegida. Gere o ELF com o dump de executável do RPCS3 (rota recomendada, docs/03) ou instale uma ferramenta local própria (scetool, detectada pelo botão **DESCRIPTOGRAFAR EBOOT.BIN (FERRAMENTA LOCAL)**). Sem o ELF com identidade aprovada, a preparação não é declarada concluída e JOGAR permanece bloqueado.
5. **PREPARAR / REPARAR** copia e verifica tudo (retomável por SHA-256; cancelamento seguro; a `PS3_GAME` original nunca é alterada) e, ao final, os campos passam a apontar para a cópia **instalada** do runtime e do ELF.
6. Escolha 60 FPS e clique **JOGAR** — cada início valida SHA-256 do runtime e do EBOOT antes de liberar.
7. Para testes, defina `DEMONS_SOULS_LAUNCHER_HOME` apontando para uma pasta isolada (não toca nas suas preferências); CLI: `--prepare-iso --iso <arquivo.iso> --destination <pasta>` (retorna 2/3/4 com o requisito exato faltando e JSON detalhado) e `--detect-tools` (diagnóstico de componentes e do ambiente de recompilação).

## 11. Verificações de integridade e identidades do runtime

O launcher recusa runtime não aprovado com "60 FPS bloqueado — Confira os caminhos em Avançado e tente novamente":

| Build | SHA-256 | Status |
|---|---|---|
| Alpha 0.2 estável | `88087725D7CB858B644D8D183329EB72E1C820B365ED2B34EBD6E2AC281F0933` | Aprovada |
| Alpha 0.2 com correção de áudio | `83B23813254C542575244F394ED31A3BE2BBD276E6A1D70372D2C6C12A77AE7E` | Aprovada (áudio validado auditivamente pelo autor) |
| Qualquer outro `des_boot.exe` | — | **Bloqueado por segurança** |

O `EBOOT.ELF` esperado tem identidade própria (SHA-256). O launcher também valida: manifesto da instalação (`installation.json` com SHA de cada componente importado), identidades NTFS/USN journal e DPAPI por máquina, e regista tudo em `logs\operations\*.jsonl` (com tamanho, hashes e fases da operação). Sem dados pessoais nos logs.

## 12. Diagnóstico rápido

| Sintoma | Causa provável | Onde olhar |
|---|---|---|
| "60 FPS bloqueado" | runtime em Avançado não é build aprovada | tabela do item 11; docs/34 |
| "A pasta escolhida precisa conter installation.json" | apontou a pasta pai | aponte a pasta do manifesto (ou faça PREPARAR) |
| Silêncio total no boot (`[audio-peak] peak=0.0000`) | corrida do libmixer (fix incorporado desde Alpha 0.2-audiofix) | `docs/34`; A/B com `scripts/audio-ab-diagnostic.ps1` |
| Executável nativo não corresponde ao SHA | manifest da instalação aponta runtime antigo | REPARAR com runtime aprovado |
| Onde ficam os logs | — | `%LOCALAPPDATA%\DemonsSoulsPCLauncher\logs\` |
| CLI reprodutível (sem janela) | — | `--fps-compatibility --installation <pasta> --profile <perfil> --runtime <exe> --elf <ELF>` → JSON com `approved`/`blocker` |
| Preparar a partir da ISO via CLI | — | `--prepare-iso --iso <arquivo.iso> --destination <pasta> [--elf <ELF>] [--runtime <exe>]` → códigos 2/3/4 = requisito exato faltando |
| Diagnóstico de ferramentas/ambiente | — | `--detect-tools [--destination <pasta>]` → scetool detectado, candidatos, pré-requisitos do build de fonte |

## 13. Testes automatizados

- `VfsPolicyTest.cs` — testes de política VFS embutidos na fonte do launcher.
- CLI do launcher: `--fps-compatibility` (gates sem GUI), `--import`, `--prepare-iso`, `--detect-tools`, `--window-test`, etc. (ver `Program.cs`).
- `scripts/audio-ab-diagnostic.ps1` — A/B de áudio do runtime (`DES_AUDIO_NOTIFY_FIX=0/1`), gerando perfil isolado com telemetria (`AUDIO_PEAK/RATE/GAPS`).
- Self-test (`--self-test`) roda em clone limpo: os manifestos de coordenação locais não distribuídos viram `SKIP` quando ausentes, e as identidades aprovadas continuam verificadas pelos demais casos; novas coberturas do assistente de ISO: escada de `EBOOT.ELF` (candidatos junto à origem/destino, decoys `EBOOT.BIN/SELF` ignorados), identidade por `PARAM.SFO`, sonda de ferramenta local (nunca fabrica caminho), diagnóstico honesto do ambiente de recompilação (ps3recomp nomeado explicitamente) e rejeição por identidade de binário fictício.
- Não há suíte "pytest/CT executável de fora" neste repo: os testes de análise/boot históricos são narrados em `docs/` com evidências (hashes, logs).

## 14. Reproduzir a correção de áudio a partir do código

1. Clone o fork `ps3recomp` no commit `add3cac07e29b4457ab24303804bfcc96056ba5a` e aplique `patches\des-audio-notify-fix.patch` (= 3 arquivos: `cellAudio.c`, `cellAtrac.c`, `ppu_hle.cpp`).
2. Compile os alvos de runtime/port (item 9.2) → `des_boot.exe`.
3. **Fixação**: no sistema do autor, a compilação exatamente reproduz o binário `83B23813254C542575244F394ED31A3BE2BBD276E6A1D70372D2C6C12A77AE7E`. Em outra máquina, o SHA pode diferir por toolchain; nesse caso, valide a SUA build (item 11 descreve como os gates da sua instalação se ajustam) e valide auditivamente.
4. A/B técnico: `DES_AUDIO_NOTIFY_FIX=1` vs `0` com `scripts/audio-ab-diagnostic.ps1`; sinaliza-chave: `[audio-notify-fix]`, `[audio-peak]`, taxas de janela "silent %".
5. **Valide auditivamente** (menu, combate, morte/respawn). Testes automáticos provam que PCM chega ao WASAPI; não provam que soa certo.

## 15. Limitações conhecidas (honestas)

- Projeto **NÃO OFICIAL**, protótipo de pesquisa; nenhuma garantia de estabilidade, desempenho ou compatibilidade.
- Área de jogo testada: boot → Nexus/1-1. O restante do jogo é desconhecido/sem testes.
- Física > 60 FPS não testada (RPCS3 a alerta); cutscenes revertem a 30 Hz acima de 30.
- Transparências passageiras (muro de neblina, aura, fog de 1 intro) somem em frames únicos.
- FPS alto citado (~120 em 1-1) é medição da máquina do autor; não é transferível.
- **O fix de áudio foi validado em um setup de áudio único** (WASAPI, Windows 10 do autor). Outras placas/drivers precisam de confirmação.
- Rede/online: nada.

## 16. Contribuindo

PRs bem-vindos para fontes (`port/`, `launcher-*`), patches sobre o `ps3recomp`, scripts/docs/testes. Regras:
- **Um problema técnico por PR**; mudanças pequenas, testadas e reversíveis (ver `AGENTS.md`).
- **Nada derivado do jogo**: `work/`, `.elf`, `.self`, `.prx`, dumps, saves, chaves, firmware, assets, screenshots/artes do jogo — absolutamente **nunca**.
- Descreva as provas reais (ambiente, data, hashes) em cada PR; não marque como "validado" algo que não foi.
- `docs/05-legal.md` define as regras non-negotiated; o mantenedor têm o derecho de que nada do jogo saia.

## 17. Licenciamento e avisos

- **Código deste project**: MIT (`LICENSE`).
- **ps3recomp** (upstream + fork JonathanDC64): MIT.
- **.NET 8**, RmlUi: MIT (avisos padrões). **Fontes OFL** (Alegreya Sans, Cinzel) e **Apache 2.0** (Material Symbols) em `port/ui/res/fonts/` com as licenças incluídas.
- **Aviso de marca**: "Demon's Souls" é marca registrada da Sony Interactive Entertainment. Este projeto é um **fan port INDEPENDENTE e NÃO OFICIAL**, sem qualquer afiliação com ou produto de Sony Interactive Entertainment, FromSoftware, Atlus ou outros detentores de direitos. Nenhuma arte, logo, armadilha ou conteúdo do jogo aparece neste repositório (regra de legalidade em `docs/05-legal.md`).
- Você é exclusivamente responsável pela sua cópia do jogo. O projeto distribui **apenas código original e ferramentas**.

---

# Demon's Souls PC — native static recompilation for Windows x64 (ENGLISH)

**Experimental native Windows x64 port of *Demon's Souls* (PS3 North America, BLUS30443) via static recompilation.** The game's PowerPC/SPU machine code is translated ahead-of-time to C, compiled, and linked against hand-written clean-room reimplementations of the PS3 system libraries. Rendering via Direct3D 12, audio via WASAPI. A WPF/.NET 8 launcher handles managed installation, SHA-256 integrity verification, and startup.

> **This repository contains no game code, game data, saves, firmware, BIOS, keys, or game executables.** You must supply your own legally-obtained copy. See [License & notices](#17-license--notices-english).

## 1. Status (2026-10-22) — Alpha 0.2, experimental

Validated on **one single reference machine** (author's Windows 10 box). None of it transfers automatically:

**Verified (reference machine):**

- Full boot up to Boletaria: logos, autosave notice, intro (sound + subtitles), title screen, character creation, tutorial, the Nexus, and Boletarian Palace 1-1.
- Lit/textured D3D12 rendering with shadows, HUD, particles, light shafts, haze; precompiled shaders.
- Save/load works, including menu sounds.
- 60 FPS profile approved via runtime identity verification (this machine also measured ~120 FPS in 1-1 with `DES_FPS=60 VBLANK_MHZ=240000` — not a general claim).
- **Complete audio-silence-at-boot bug fixed**: root cause documented (libmixer notify-queue race), fix published as `patches/des-audio-notify-fix.patch`; automatically validated by A/B runs (PCM reaches WASAPI with the fix ON and does not with it OFF; 3752/3752 and 8442/8442 notifications across two test sets — details in `docs/34`) **and audibly validated by the author on 2026-10-22 on an isolated installation** (runtime `83B238…`).
- The Alpha 0.2 launcher: builds, opens, imports/repairs managed installations, enforces identity gates (unapproved runtimes are blocked), records every operation as JSONL traces.

**Not guaranteed / open:**

- Nothing about FPS, sound, or stability is guaranteed on any other machine.
- Only the first area is verified — the rest of the game is untested. Physics above 60 FPS untested; cutscenes revert to 30 Hz above 30. Some transparent effects vanish for single frames. Audio validated on ONE setup only. No online.

## 2. Architecture

```
(user, once)                          (tooling)
game EBOOT.BIN (SELF) ──RPCS3/scetool──▶ decrypted EBOOT.ELF (PowerPC64 BE)
                                                │
                                                ▼
            ps3recomp upstream + patches/des-audio-notify-fix.patch
            (PPU/SPU → AOT C, SPU image extraction, NID imports)
                                                │
                                                ▼
                        native runtime des_boot.exe (x64)
                        reimplemented PS3 libs + port/ layer
                                                ▲
                                                │ validated launch (SHA-256)
            launcher DemonsSouls.Launcher.exe (WPF/.NET 8)
            PREPARE/REPAIR → vfs/PS3_GAME + runtime/EBOOT.ELF + EXE
            installation.json manifest + JSONL OperationTrace
```

Repository contents: `patches/` (our fix on the MIT-licensed upstream [ps3recomp](https://github.com/sp00nznet/ps3recomp), fork [JonathanDC64/ps3recomp](https://github.com/JonathanDC64/ps3recomp)); `port/` (CMake, RmlUi in-game UI with OFL/Apache fonts included, heap, menu SFX, stubs); `launcher-alpha02-hotfix/` (full launcher source); `launcher/` (legacy original launcher); `docs/` (35 technical documents); `scripts/` and `tools/` (build/analysis tooling). Game-derived folders (`work/`, game assets, saves) stay local and gitignored, always.

## 3. Build (dev)

```powershell
# launcher (single reproducible command)
dotnet publish launcher-alpha02-hotfix\DemonsSouls.Launcher.csproj -c Release -r win-x64 --self-contained true -o ..\launcher-build

# runtime: clone https://github.com/JonathanDC64/ps3recomp at commit
# add3cac07e29b4457ab24303804bfcc96056ba5a, apply patches\des-audio-notify-fix.patch,
# provide a locally decrypted EBOOT.ELF from your own copy (RPCS3 or scetool),
# then build the CMake targets (see docs/03-setup.md). No one-command build-all.
```

## 4. Approved runtime identities

| Build | SHA-256 | Status |
|---|---|---|
| Alpha 0.2 stable | `88087725D7CB858B644D8D183329EB72E1C820B365ED2B34EBD6E2AC281F0933` | Approved |
| Alpha 0.2 audio fix | `83B23813254C542575244F394ED31A3BE2BBD276E6A1D70372D2C6C12A77AE7E` | Approved (audibly validated by the author) |

Any other `des_boot.exe` is blocked on purpose ("60 FPS blocked — check the paths under Advanced and try again").

## 5. Running

Open the launcher → **PREPARE** (imports your `PS3_GAME` copy into a managed installation) or **REPAIR** → under **Advanced**, point to an approved `des_boot.exe` and the `EBOOT.ELF` from your copy → 60 FPS → **PLAY**. Every start verifies SHA-256 before releasing the button. For isolated testing set `DEMONS_SOULS_LAUNCHER_HOME`. Reproducible CLI without GUI: `--fps-compatibility` (JSON verdict), `--import`, `--repair`.

## 6. Diagnostics (common symptoms)

- **"60 FPS blocked"** — the runtime under Advanced is not an approved build (table above).
- **"The selected folder must contain installation.json"** — point at the manifest subfolder, or run PREPARE.
- **Silence at boot / `[audio-peak] peak=0.0000`** — the documented libmixer race; A/B reproduce via `scripts/audio-ab-diagnostic.ps1` (`DES_AUDIO_NOTIFY_FIX=0/1`).
- Logs: `%LOCALAPPDATA%\DemonsSoulsPCLauncher\logs\` (`launcher.log` + `operations\*.jsonl`).

## 7. Reproducing the audio fix

1. Clone the `ps3recomp` fork at commit `add3cac07e29b4457ab24303804bfcc96056ba5a`; apply `patches\des-audio-notify-fix.patch` (3 files: `cellAudio.c`, `cellAtrac.c`, `ppu_hle.cpp`).
2. Build the runtime/port CMake targets → your `des_boot.exe`.
3. On the author's toolchain the build reproduces binary `83B23813254C542575244F394ED31A3BE2BBD276E6A1D70372D2C6C12A77AE7E` byte-exactly; other toolchains may differ — validate YOUR build and listen to it.
4. A/B: `DES_AUDIO_NOTIFY_FIX=1` vs `0`; markers `[audio-notify-fix]`, `[audio-peak]`, per-window "silent %".
5. **Listen to it yourself** (menus, combat, death/respawn). Automated tests prove PCM flows into WASAPI; they do not prove it sounds right.

## 8. Limitations (honest)

Experimental research prototype, no guarantees; only the first area verified; physics > 60 FPS untested; some transparency glitches; high-FPS numbers are reference-machine measurements; audio fix validated on one audio setup; no online; solo-developer effort quality varies.

## 9. Contributing

PRs welcome for `port/`, launcher sources, patches on `ps3recomp`, scripts/docs/tests. House rules: one technical issue per PR; small, tested, reversible changes; **absolutely nothing game-derived** (no `work/`, `.elf`, `.self`, `.prx`, dumps, saves, keys, firmware, assets, game screenshots); real test evidence in every PR (environment, date, result); see `AGENTS.md` and `docs/05-legal.md`.

## 10. License & notices

This project's code: MIT (`LICENSE`). Upstream ps3recomp: MIT. .NET 8 and RmlUi: MIT with standard notices. Fonts: OFL (Alegreya Sans, Cinzel) and Apache 2.0 (Material Symbols), license files included in `port/ui/res/fonts/`.

**Trademark notice:** "Demon's Souls" is a trademark of Sony Interactive Entertainment. This is an **INDEPENDENT, UNOFFICIAL fan project** — no affiliation with, sponsorship of, or endorsement by Sony Interactive Entertainment, FromSoftware, Atlus, or any rights holder. No artwork, logos, screenshots or game content appear in this repository. You are solely responsible for your own game copy; the project ships only original code and tooling.
