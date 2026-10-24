# Alternância de áudio com Insert + sincronização com o launcher (2026-10-24)

Solução temporária do bug de áudio (boots com PCM todo-zero) enquanto a causa raiz é
investigada. Escopo: controle de mute acessível (jogo + launcher), preservando o
subsistema de áudio, o clock do jogo e todos os ativos chamados pela missão
(GameData, saves, Alpha exes, launcher V14).

## Arquivos modificados

1. `port/ui/src/des_ui.cpp` — hotkey `Insert` + publicação de estado
   - `audio_state_publish` / `audio_mute_current` / `audio_set_mute` (helpers no namespace
     anônimo, após `is_menu_key`).
   - Caso `VK_INSERT` (`0x2D`) em `wndproc`, antes do roteamento de teclas da UI:
     só `WM_KEYDOWN` sem o bit 30 (auto-repeat) atua; a tecla é consumida em
     `WM_KEYDOWN`/`WM_KEYUP` (`*result = 0; return 1`), então o jogo recompilado e o
     RmlUi nunca a veem (Insert não tem função no jogo).
   - Publicação de estado inicial em `frame()` (guard `static std::atomic<bool>`), para o
     launcher conhecer o valor mesmo sem toggle.
   - `ps3_setenv` declarado no bloco `extern "C"` existente (boot_main.cpp / cache getenv).
2. `launcher/MainWindow.cs` — indicador + sincronização (poll de 1 s)
   - `AudioState` (TextBlock) na aba de configurações: "Áudio: ATIVADO/DESATIVADO".
   - `DispatcherTimer` de 1000 ms (`ReflectAudioState`) + guard `applyingFromGame` nos
     eventos `Checked/Unchecked` do checkbox existente "Silenciar áudio".
   - Jogo rodando (processo `des_boot`): estado reflete `audio-state.txt` (fonte de
     verdade = jogo). Sem jogo: estado reflete a preferência salva (checkbox), que o
     launcher já aplica via env `AUDIO_MUTE` na inicialização (`RuntimeService`).

Nenhum build-file foi editado. Os arquivos pré-modificados no worktree
(`port/CMakeLists.txt`, `port/des_menu_sfx.cpp`) permanecem intocados por esta missão.

## Mecanismo de silenciamento (não é pausa da simulação)

`AUDIO_MUTE` é o knob vivo já existente do port (`ps3recomp/libs/audio/cellAudio.c,
~linha 399, `PS3_KNOB(mute, ...)`): com "1", o mix submete silêncio ao dispositivo WASAPI
na taxa normal. O relógio de áudio e o pacing do jogo não mudam (diferente de
`PS3_NO_AUDIO`, que segue o caminho sem áudio do título). Nada é destruído ou recriado:
nenhum buffer descartado, nenhuma thread parada, nenhum dispositivo reaberto.

- `audio_set_mute` chama `ps3_setenv("AUDIO_MUTE", ...)` diretamente: o launcher sempre
  envia `AUDIO_MUTE` na env de partida (`RuntimeService` linha 88), o que marca a chave
  como *pinned*; nesse estado `ui::settings::set` aplicaria só o arquivo, não a env.
  O `ps3_setenv` (via `cached_setenv`: SRW lock + bump de `ps3_env_gen`) alcança o mix no
  próximo bloco (≤ 5,33 ms).
- `ui::settings::set` também é chamado para manter o menu in-game ("Sound", que mostra
  esse knob invertido) e `settings.ini` coerentes. Com chave pinned, a badge do menu
  continua mostrando o valor da env do launcher — divergência cosmética documentada.
- A tecla é consumida em ambos os estados (menu aberto ou jogo): Insert muda o áudio em
  qualquer momento da sessão (com `DES_UI=1`, overlay ativo).

## Sincronização launcher ↔ jogo

Arquivo de estado: `%APPDATA%\DemonsSoulsRecomp\DemonsSouls\audio-state.txt`
(`report::data_dir()`; escrita atômica tmp + `MoveFileExW`, mesmo idioma de
`settings::save`). Conteúdo: `"0"` (áudio on) ou `"1"` (mudo), seguido de `\n`.

- Jogo → launcher: publicação inicial no primeiro `frame()` + a cada toggle. O launcher
  lê com `DispatcherTimer` de **1 s** (não-agressivo), com detect do processo `des_boot`.
- Launcher → jogo **durante a partida**: não implementado. A env só é lida no spawn do
  processo; um canal de controle ao vivo (injeção/IPC/memória compartilhada) seria
  invasivo demais para um hotfix. **Limitação aceita pela missão.** O usuário alterna ao
  vivo com `Insert`; mudanças no launcher valem na próxima inicialização.
- Runs de diagnóstico/harness (`DES_UI=0`, `PS3_PIDFILE`): o overlay não é instalado e
  `data_dir` fica vazio ⇒ nem hotkey nem arquivo de estado existem (por design).
- Sessões iniciadas por `Iniciar-Alpha02-60FPS.ps1`/`Iniciar-Alpha02-Teste.ps1` setam
  `DES_DATA_DIR` próprio (`$Profile\interface`): o estado é escrito lá, e o launcher
  (que só conhece o diretório padrão `%APPDATA%\DemonsSoulsRecomp\DemonsSouls`) mostra
  "aguardando o estado do jogo" — degradação graciosa, sem erro. Para o indicador vivo,
  iniciar o jogo pelo launcher (ele não define `DES_DATA_DIR`).

## Persistência

- Launcher: checkbox "Silenciar áudio" + JSON já existente
  (`%LOCALAPPDATA%\DemonsSoulsPCLauncher\settings.json`); padrão = áudio ligado
  (`Mute=false` → env `AUDIO_MUTE=0`).
- Jogo: `ui::settings::set` grava o último valor em `settings.ini`; na partida seguinte
  a env do launcher ganha (pinned) — a preferência do launcher governa o boot.

## Diagnóstico da causa raiz (bug original: boots com PCM todo-zero)

O toggle cria um marcador no log que permite comparar o subsistema antes/depois da
alternância, sem dados pessoais, usando a telemetria que já existe:

- `[audio-device] ...` (cellAudio.c, a cada `report_blocks` do mix): `nonzero_blocks`,
  `peak`, `muted`, drops, padding. Comparar janela **antes vs depois** de cada
  `[audio-toggle]`:
  - `muted=1` e `peak` medido no mixer não-nulo ⇒ mix vivo, silêncio esperado (knob ok).
  - `muted=0` e `nonzero_blocks=0` após boot ⇒ o mix recebe PCM todo-zero do guest
    (caminho do bug: notify zero-key / corrida de `audio_notify_event_queues` /
    `DES_AUDIO_NOTIFY_FIX`).
- `[audio-toggle] Insert: AUDIO_MUTE=...` marca o instante exato de cada mudança.
- Se um boot silencioso **ré-aquece com Insert** (som volta depois do toggle 0→0→0?), o
  estado de notify/filas não está perdido — aponta para o lado do guest/PWM; se
  reativação imediata falhar, suspeitar do caminho WASAPI/padding.
- Contexto prévio: `recovery/coordination/NEXUS-AUDIO-FIX.md`,
  `recovery/coordination/AUDIO-FLOW-20261020.md` ( corrida corrigida no leitor de
  notify; boots silenciosos persistiram mesmo com a recuperação desligada).

## Quadro de teste manual (lista do operador, 2026-10-24)

Status da sessão de validação. Itens 6 e 7 têm a parte de *plumbing* coberta pelo
`--self-test` do launcher (asserts de estado do arquivo → checkbox/indicador, ramos
"jogo rodando" e "sem jogo", guard anti-recursão): qualquer falha abortaria a suíte com
`FAIL` + exit 1. Registro: `SELF_TEST_PASS` + `EXITCODE=0`
(`launcher\bin\Debug\net8.0-windows\DemonsSouls.Launcher.exe --self-test`, 2026-10-24).

| # | Item | Procedimento | Evidência | Status |
|---|------|--------------|-----------|--------|
| 1 | Inicialização | Jogar pelo launcher com mute **desmarcado** (env `AUDIO_MUTE=0`) | som de boot; `audio-state.txt` nasce com `0` | [ ]# |
| 2 | Silenciar | 1 toque em `Insert` durante a partida | `[audio-toggle] Insert: AUDIO_MUTE=1 (muted)`; arquivo → `1`; som cessa ≤ 1 bloco (~5,33 ms) | [x] |
| 3 | Restaurar áudio | 2º toque em `Insert` | `AUDIO_MUTE=0 (sound on)`; arquivo → `0`; volume original, sem amplificação | [x] |
| 4 | Repetição da tecla | Segurar `Insert` (auto-repeat) | 1 linha `[audio-toggle]` por pressão (bit 30 filtra) | [~]† |
| 5 | Entrada do jogo | Jogar: WASD/gamepad/menus com `Insert` disponível | jogabilidade incólume; tecla consumida | [ ]# |
| 6 | Indicador do launcher | Reabrir o launcher com o jogo rodando; depois com o jogo fechado | com jogo: segue `audio-state.txt` (≤ 1 s); sem jogo: segue checkbox | [x]* |
| 7 | Persistência | Alternar checkbox e salvar; relançar | env `AUDIO_MUTE` na `launch-*.json` = checkbox; padrão 1ª execução = ligado | [x]* |
| 8 | Telemetria | Sessão com 2+ alternâncias | `[audio-device]` antes/depois sem quedas de `calls`/clock; sem dados pessoais | [ ]‡ |

\* itens 6/7: *plumbing* automatizado no `--self-test` (o indicador visual durante a
partida real permanece na observação humana da sessão).
\# itens 1/5: observação humana da sessão jogada (os logs mostram sessão normal).
† item 4: análise de intervalos no log (`WAIT_SIGNAL` = 500 ms): máximo de **6
alternâncias/s** observado; um auto-repeat vazado deixaria marcas ≥ 12/s (typematic do
Windows ~25–30 cps) — evidência favorável ao filtro; certificação final = segurar
`Insert` ~3 s e conferir **1** linha `[audio-toggle]` por hold.
‡ item 8: o repórter `[audio-device]` só roda com `AUDIO_STAGES=1`
(`cellAudio.c` `ps3_env_int("AUDIO_STAGES", 0)`; 938 blocos ≈ 5 s por linha; com
`AUDIO_NEXUS_TRACE=1`: 188 blocos ≈ 1 s + `[audio-nexus-energy]`). O launcher só passa
as chaves `AUDIO_*` que ele mesmo define (RuntimeService remove todas as `AUDIO_*` do
espaço de env antes de setar as suas) — sem pass-through — logo uma sessão de
telemetria exige estender o `RuntimeService`/`--play-test` com o knob (ou rodar o exe
via script com o env completo). Registrada como pendência de ambiente, não de código.

### Sessão executada (2026-10-24, `--play-test` isolado)

Run `F:\DemonSouls-PC\GameData\tests\run-20261024-114259-023` · PID 2836 · runtime =
build novo (283 MB) · verificação fast-path 2,8 s (12.512 arquivos) · 1920×1080
borderless (fallback de monitor documentado no `window-state.jsonl`). Sessão de ~3,5
min: **19 alternâncias `Insert`** (intercalada com blocos `WAIT_SIGNAL`; estritamente
alternantes 1↔0), publicação de estado em `audio-state.txt` a cada mudança
(0↔“1”@11:45:55), fechamento limpo do jogo (`[boot] window closed -- exiting`), nenhum
marcador de crash/fault. `settings.ini` carregado do caminho padrão esperado. O estado
final ficou `1` (mudo) — sem efeito na preferência do launcher.

## Reversão

Reverter os dois arquivos (`git checkout -- port/ui/src/des_ui.cpp
launcher/MainWindow.cs` — ATENÇÃO: preservar as mudanças pré-existentes de
`port/CMakeLists.txt`/`des_menu_sfx.cpp`, que não foram tocadas) e RELINKAR o
executável no mesmo diretório de build. A build instalada da Alpha e o GameData nunca
foram alterados por esta missão.
