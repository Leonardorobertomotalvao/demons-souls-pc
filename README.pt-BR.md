[English](README.md) | Português (BR)

# Demon's Souls PC — recompilação estática nativa para Windows x64

**Porte experimental não oficial de *Demon's Souls* (PS3, América do Norte, BLUS30443) para Windows x64 nativo, pela técnica de recompilação estática.** O código PowerPC/SPU do jogo é convertido para C antecipadamente (AOT), compilado e ligado a uma reimplementação (clean-room) das bibliotecas de sistema do PS3 — sem emulação general-purpose em tempo de execução. Renderização via Direct3D 12, áudio via WASAPI, e um launcher em WPF/.NET 8 para instalação gerenciada, verificação de integridade SHA-256 e inicialização.

Este projeto é para pessoas que têm uma cópia legal do jogo e querem jogá-lo nativamente num PC com Windows moderno. **Não** é um emulador e não distribui **nenhum** conteúdo do jogo: você fornece os arquivos do seu próprio exemplar.

[![Release](https://img.shields.io/github/v/release/Leonardorobertomotalvao/demons-souls-pc)](https://github.com/Leonardorobertomotalvao/demons-souls-pc/releases/latest)
[![Status](https://img.shields.io/badge/status-alpha--experimental-orange)](#status--roadmap)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Stars](https://img.shields.io/github/stars/Leonardorobertomotalvao/demons-souls-pc)](https://github.com/Leonardorobertomotalvao/demons-souls-pc/stargazers)
[![Issues](https://img.shields.io/github/issues/Leonardorobertomotalvao/demons-souls-pc)](https://github.com/Leonardorobertomotalvao/demons-souls-pc/issues)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-blue)](#requisitos)

> **Este repositório não contém código do jogo, dados do jogo, saves, firmware, BIOS, chaves ou executáveis do jogo.** Você precisa da sua própria cópia legal. Veja [Aviso legal](#aviso-legal).
>
> **Histórico de versões do launcher:** [CHANGELOG.md](CHANGELOG.md).

## Recursos

Tudo abaixo foi **verificado em uma única máquina de referência** (Windows 10 do autor — Ryzen 5 5500, 16 GB RAM, HDD). Nada se transfere automaticamente para outras máquinas; veja [Status / Roadmap](#status--roadmap).

- **Inicialização completa até Boletaria**: logos, aviso de auto-save, abertura com som e legendas, tela-título, criação de personagem, tutorial, o Nexus e o Palácio Boletariano 1-1.
- **Renderização D3D12**: cena iluminada e texturizada, sombras, HUD, partículas, *light shafts* e haze; shaders pré-compilados (o jogo não pausa para compilar shader).
- **Save/load funcionando**, incluindo sons de menu.
- **Perfil de runtime a 60 FPS**, aprovado por verificação de identidade. Na máquina de referência, 1-1 com `DES_FPS=60` e `VBLANK_MHZ=240000` mediu ~120 FPS — não é uma afirmação geral.
- **Bug de silêncio total de áudio na inicialização corrigido na causa raiz**: corrida da fila de eventos do libmixer, documentada em `docs/34-audio-notify-startup-race.md`, corrigida por `patches/des-audio-notify-fix.patch`, validada por A/B (o PCM chega ao WASAPI com o fix ligado e não chega desligado; 3752/3752 e 8442/8442 notificações em dois conjuntos de testes) e auditivamente pelo autor numa instalação isolada.
- **Launcher (Alpha 0.2)**: importa/prepara/repara uma instalação gerenciada a partir da sua ISO local ou cópia `PS3_GAME`, aplica gates de identidade (runtime não aprovado é bloqueado) e registra cada operação em traces JSONL. Sobre instalação íntegra, a verificação completa leva ~4 s lendo **0 bytes** (confirmação por identidade NTFS/USN); re-preparar com a mesma ISO leva ~9 s.
- **Arquitetura**: boot PPU completo, imagens SPU recompiladas, SPURS HLE com coordenação de tarefas, reimplementações de cellAudio/cellAtrac/cellFs/camera/input, UI no jogo via RmlUi.

## Status / Roadmap

**Estado atual: Alpha 0.2 — protótipo experimental.** Validado em uma máquina de referência apenas. Não há garantias de estabilidade, desempenho ou compatibilidade em qualquer outra máquina.

**Em desenvolvimento** (itens abertos):

- Transparências que desaparecem em frames únicos (muro de neblina, aura de alma, neve).
- Gerenciador de cutscenes acima de 30 Hz.
- Validação de física acima de 60 FPS.
- Mais controles (teclado+mouse e DualSense USB/BT-HID são os testados; outros pads não).
- Configuração persistente do usuário.

**Não implementado / limites conhecidos:**

- Online/rede: nada implementado.
- Apenas a primeira área está verificada — o restante do jogo não foi testado.
- LLE completo de SPURS/SPU: um experimento anterior foi abandonado (`docs/22-libsre-lle-attempt.md`); o runtime usa HLE + imagens SPU recompiladas.
- Cutscenes voltam a 30 Hz acima de 30; física > 60 FPS sem testes (o jogo original roda a 30).

Histórico e crônica técnica do projeto: `docs/00-feasibility.md` até `docs/35-launcher-prepare-optimization.md`.

## Requisitos

### Para jogar (pacote do launcher pré-compilado)

- Windows 10/11 x64; GPU com D3D12 feature level 12.0+.
- Pacote self-contained do launcher das [Releases](https://github.com/Leonardorobertomotalvao/demons-souls-pc/releases/latest) — não precisa instalar .NET.
- **Sua cópia legal do jogo** (PS3 USA, BLUS30443) e um `EBOOT.ELF` decriptado **a partir da sua própria cópia** — ex.: o dump de executável do RPCS3, ou `scetool` com chaves públicas. O projeto nunca decripta nada por você e nunca distribui decriptação.
- Espaço em disco para a instalação gerenciada (cópia dos dados do jogo + vfs + perfil).

### Para compilar do fonte

- Windows 10/11 x64; VS 2022 Build Tools (C++ + Windows SDK com `d3d12.h`), CMake ≥ 3.20, Ninja e o clang usado pelo fork.
- Python 3.11+ com `ps3recomp/requirements.txt` (pycryptodome, capstone, construct, tabulate, tqdm) para o tooling de análise.
- .NET SDK 8 (launcher).
- RPCS3 (ou scetool com chaves públicas lv0/metldr) para decriptar **a sua própria cópia** — o projeto não distribui decriptação nem chaves.
- Ghidra opcional (análise).

## Instalação

### Usuários finais (launcher pré-compilado)

1. Baixe `DemonSoulsPC-Launcher-Windows-x64-<versão>.zip` das [Releases](https://github.com/Leonardorobertomotalvao/demons-souls-pc/releases/latest) e extraia **o ZIP inteiro** numa pasta gravável, mantendo a pasta `Launcher\` junta.
2. Execute `Launcher\DemonsSouls.Launcher.exe`.
3. **SELECIONAR ISO LOCAL** — o launcher monta a sua imagem em modo somente leitura, identifica a edição pelo `PARAM.SFO` (BLUS30443 · 01.00) e preenche a pasta de origem. Se o Windows recusar a montagem, dê duplo clique na ISO no Explorador e repita.
4. Você também precisa fornecer: um runtime `des_boot.exe` **aprovado** e o `EBOOT.ELF` da sua cópia decriptada (rota recomendada: dump de executável do RPCS3). O launcher **não** descriptografa `EBOOT.BIN` protegida; sem a identidade do ELF aprovada, o JOGAR fica bloqueado.
5. Clique em **PREPARAR** (ou **REPARAR**) — tudo é copiado e verificado para uma instalação gerenciada (retomável por SHA-256; a sua pasta `PS3_GAME` original nunca é alterada).
6. Em **Avançado**, confirme os caminhos, escolha 60 FPS e clique **JOGAR** — cada início valida o SHA-256 do runtime e do EBOOT antes de liberar o botão.
7. Para testes isolados, defina `DEMONS_SOULS_LAUNCHER_HOME` apontando para uma pasta separada. O launcher nunca mexe nas suas preferências.

### Do fonte

O launcher tem um build único reproduzível:

```powershell
git clone https://github.com/Leonardorobertomotalvao/demons-souls-pc demons-souls-pc
cd demons-souls-pc
dotnet publish launcher-alpha02-hotfix\DemonsSouls.Launcher.csproj -c Release -r win-x64 --self-contained true -o ..\launcher-build
# → ..\launcher-build\DemonsSouls.Launcher.exe
```

O runtime **não** é um build de um botão: clone o fork [ps3recomp](https://github.com/JonathanDC64/ps3recomp) no commit `add3cac07e29b4457ab24303804bfcc96056ba5a`, aplique `patches/des-audio-notify-fix.patch`, configure o MSVC (`scripts/vsenv.bat`), coloque os arquivos do seu jogo decriptado em `work\game\` (conforme `docs/03-setup.md`) e compile os alvos CMake (port + runtime) para x64 → `port\build\des_boot.exe`.

**Honestidade sobre reprodutibilidade:** no toolchain do autor, o build do fix de áudio reproduz byte a byte `83B23813254C542575244F394ED31A3BE2BBD276E6A1D70372D2C6C12A77AE7E`; outros toolchains podem divergir. O launcher aprova runtimes por identidade (SHA-256) — para rodar a SUA build, reproduza os próprios gates da sua instalação.

## Uso

### Launcher

Abra `DemonsSouls.Launcher.exe` → o assistente da aba **Instalação** mostra cada requisito em tempo real (imagem/origem, `EBOOT.ELF` preparado, executável nativo aprovado, destino, espaço em disco) → **PREPARAR / REPARAR** → caminhos em **Avançado** → 60 FPS → **JOGAR**.

### Linha de comando (reprodutível, sem GUI)

```text
--fps-compatibility --installation <dir> --profile <p> --runtime <exe> --elf <ELF>   → veredito JSON (approved / blocker)
--prepare-iso --iso <arquivo.iso> --destination <dir> [--elf <ELF>] [--runtime <exe>] → códigos de saída 2/3/4 = requisito exato faltando
--verify --installation <dir>                                                        → verificação de integridade completa
--import, --repair, --detect-tools, --self-test, --window-test
```

O `--prepare-iso` também aceita `--source-image` para reutilizar a identidade da ISO registrada anteriormente (segundos em vez de minutos nas repetições).

### Identidades de runtime aprovadas

O launcher recusa runtime não aprovado ("60 FPS bloqueado — confira os caminhos em Avançado e tente novamente"):

| Build | SHA-256 | Status |
|---|---|---|
| Alpha 0.2 estável | `88087725D7CB858B644D8D183329EB72E1C820B365ED2B34EBD6E2AC281F0933` | Aprovada |
| Alpha 0.2 com correção de áudio | `83B23813254C542575244F394ED31A3BE2BBD276E6A1D70372D2C6C12A77AE7E` | Aprovada (áudio validado auditivamente pelo autor) |
| Qualquer outro `des_boot.exe` | — | **Bloqueado por segurança** |

### Diagnóstico rápido (sintomas comuns)

| Sintoma | Causa provável | Onde olhar |
|---|---|---|
| "60 FPS bloqueado" | runtime em Avançado não é build aprovada | tabela de identidades acima; `docs/34` |
| "A pasta escolhida precisa conter installation.json" | apontou a pasta pai | aponte a pasta do manifesto, ou faça PREPARAR |
| Silêncio total no boot (`[audio-peak] peak=0.0000`) | corrida do libmixer (corrigida desde Alpha 0.2-audiofix) | `docs/34`; A/B com `scripts/audio-ab-diagnostic.ps1` |
| Executável nativo não corresponde ao SHA | manifesto da instalação aponta runtime antigo | REPARAR com runtime aprovado |
| Onde ficam os logs | — | `%LOCALAPPDATA%\DemonsSoulsPCLauncher\logs\` |

## Perguntas frequentes (FAQ)

**Isto é um emulador?**
Não. A recompilação estática traduz o código de máquina do jogo uma única vez, antecipadamente, para código x64 nativo. Não há emulação general-purpose de CPU em tempo de execução — as bibliotecas do PS3 são reimplementadas nativamente, e vídeo/áudio passam por Direct3D 12/WASAPI.

**O download inclui o jogo?**
Não. A página de Releases contém apenas o launcher e ferramentas — sem código do jogo, sem dados, sem firmware, sem chaves. Você fornece a sua própria cópia legal, e o launcher verifica identidades antes de deixar jogar.

**De onde baixo o jogo?**
Daqui, não. Este projeto exige uma cópia do jogo de PS3 que você possua legalmente; você decripta o próprio EBOOT localmente, na sua máquina (dump de executável do RPCS3 ou scetool com chaves públicas). O projeto não distribui decriptação nem chaves.

**Roda mesmo a 60 FPS? O original é 30.**
Existe um perfil de 60 FPS aprovado, com gate de identidade de runtime. Na máquina de referência, a área 1-1 mediu ~120 FPS com ajustes específicos — essa medição não se transfere para outras máquinas, e física acima de 60 FPS não foi testada.

**Por que o meu `des_boot.exe` é bloqueado?**
O launcher aplica gates de identidade: apenas builds com SHA-256 aprovado rodam com 60 FPS. É de propósito — impede executáveis desconhecidos ou adulterados. Veja a tabela de identidades acima.

**Posso contribuir com um build do runtime?**
Builds do runtime são pessoais (compilados a partir da sua cópia decriptada); não adianta anexá-los numa issue. Patches, código do port, código do launcher, scripts e docs são bem-vindos — veja [Contribuindo](#contribuindo).

**Isto tem afiliação com Sony, FromSoftware ou Atlus?**
Não. É um fan project independente e não oficial, sem qualquer afiliação, patrocínio ou endosso dos detentores de direitos. Veja o [Aviso legal](#aviso-legal).

## Capturas de tela

<!-- Política de screenshots: este repositório deliberadamente não contém material
     derivado do jogo (veja o Aviso legal). É seguro commitar: screenshots da própria
     UI do launcher (seu software), colocados em docs/screenshots/ ou .github/assets/.
     Capturas dentro do jogo: hospede externamente e linke aqui por URL, ou mude
     conscientemente a política declarada do projeto antes. -->

Quando disponíveis, aparecerão aqui:

<!-- <p align="center">
  <img src="docs/screenshots/launcher-installation-tab.png"
       alt="Aba de instalação do launcher Demon's Souls PC, com o assistente de preparação mostrando origem ISO, runtime aprovado e destino verificados" width="700">
</p>
<p align="center">
  <img src="docs/screenshots/launcher-play-60fps.png"
       alt="Aba avançado do launcher com des_boot.exe aprovado e caminhos do EBOOT.ELF confirmados e o botão de 60 FPS selecionado antes de JOGAR" width="700">
</p> -->

## Contribuindo

PRs são bem-vindos para `port/`, fontes do launcher, patches sobre o `ps3recomp`, scripts, docs e testes. Regras da casa (detalhes em [CONTRIBUTING.md](CONTRIBUTING.md) e `AGENTS.md`):

- Um problema técnico por PR; mudanças pequenas, testadas e reversíveis.
- **Nada derivado do jogo, absolutamente**: sem `work/`, `.elf`, `.self`, `.prx`, dumps, saves, chaves, firmware, assets ou screenshots do jogo — nunca.
- Descreva as provas reais (ambiente, data, hashes) em cada PR; não marque como "validado" algo que não foi.

Relatos de bug e pedidos de funcionalidade: use os templates de issue ([bug report](https://github.com/Leonardorobertomotalvao/demons-souls-pc/issues/new?template=bug_report.yml) · [feature request](https://github.com/Leonardorobertomotalvao/demons-souls-pc/issues/new?template=feature_request.yml)); dúvidas abertas e ajuda de configuração vão para as [Discussions](https://github.com/Leonardorobertomotalvao/demons-souls-pc/discussions).

## Aviso legal

- **Este é um fan project não oficial** — protótipo de pesquisa. Sem garantia de estabilidade, desempenho ou compatibilidade de qualquer tipo.
- **Nenhum conteúdo do jogo é incluído ou distribuído aqui.** O repositório distribui apenas código original e ferramentas. O código de máquina e os dados do jogo continuam sendo seus: você os decripta localmente, na sua máquina, para interoperabilidade, a partir da sua própria cópia. Chaves, dumps e assets nunca são comitados, publicados ou compartilhados.
- **Aviso de marca:** "Demon's Souls" é marca registrada da Sony Interactive Entertainment. Este projeto é um fan port independente e não oficial, sem qualquer afiliação ou endosso da Sony Interactive Entertainment, FromSoftware, Atlus ou outros detentores de direitos. Nenhuma arte, logo ou conteúdo do jogo aparece neste repositório.
- Você é exclusivamente responsável pela sua cópia do jogo. As regras do projeto estão em `docs/05-legal.md`.
- **Licença:** o código deste projeto é MIT ([LICENSE](LICENSE)). ps3recomp upstream: MIT. .NET 8 e RmlUi: MIT com avisos padrão. Fontes: OFL (Alegreya Sans, Cinzel) e Apache 2.0 (Material Symbols), com as licenças incluídas em `port/ui/res/fonts/`.

## Links

- **Site:** https://leonardorobertomotalvao.github.io/demons-souls-pc/
- **Fonte do site:** [`docs/`](docs/) — site estático (HTML + CSS + JS mínimo, sem build), publicado pelo GitHub Pages a partir de `main` + `/docs`. Os metadados de SEO (meta tags, sitemap, JSON-LD, hreflang) ajudam na descoberta; a indexação e as posições dependem exclusivamente dos buscadores — não há garantia de indexação ou de posições.
- **Repositório:** https://github.com/Leonardorobertomotalvao/demons-souls-pc
- **Releases (pacotes do launcher):** https://github.com/Leonardorobertomotalvao/demons-souls-pc/releases
- **Discussions:** https://github.com/Leonardorobertomotalvao/demons-souls-pc/discussions
- **Issues:** https://github.com/Leonardorobertomotalvao/demons-souls-pc/issues
- **Changelog:** [CHANGELOG.md](CHANGELOG.md)
- **Crônica técnica:** [`docs/`](docs/) — 35 documentos, da viabilidade à otimização da preparação do launcher
- **Tooling upstream:** [ps3recomp](https://github.com/sp00nznet/ps3recomp) (MIT) · fork [JonathanDC64/ps3recomp](https://github.com/JonathanDC64/ps3recomp) (MIT)
