English | [Português (BR)](README.pt-BR.md)

# Demon's Souls PC — native static recompilation for Windows x64

**An unofficial, experimental native Windows port of *Demon's Souls* (PS3 North America, BLUS30443), built with static recompilation.** The game's PowerPC/SPU machine code is translated ahead-of-time to C, compiled to a native x64 executable, and linked against hand-written clean-room reimplementations of the PS3 system libraries — no general-purpose emulation at runtime. Rendering uses Direct3D 12, audio uses WASAPI, and a WPF/.NET 8 launcher handles managed installation, SHA-256 integrity verification, and startup.

This project is for people who own a legal copy of the game and want to play it natively on a modern Windows PC. It is **not** an emulator and ships **no game content**: you supply the game files yourself, from your own copy.

[![Release](https://img.shields.io/github/v/release/Leonardorobertomotalvao/demons-souls-pc)](https://github.com/Leonardorobertomotalvao/demons-souls-pc/releases/latest)
[![Status](https://img.shields.io/badge/status-alpha--experimental-orange)](#status--roadmap)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Stars](https://img.shields.io/github/stars/Leonardorobertomotalvao/demons-souls-pc)](https://github.com/Leonardorobertomotalvao/demons-souls-pc/stargazers)
[![Issues](https://img.shields.io/github/issues/Leonardorobertomotalvao/demons-souls-pc)](https://github.com/Leonardorobertomotalvao/demons-souls-pc/issues)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-blue)](#requirements)

> **This repository contains no game code, game data, saves, firmware, BIOS, keys, or game executables.** You must supply your own legally-obtained copy. See [Legal disclaimer](#legal-disclaimer).
>
> **Launcher version history:** [CHANGELOG.md](CHANGELOG.md).

## Features

All items below were **verified on one single reference machine** (the author's Windows 10 box — Ryzen 5 5500, 16 GB RAM, HDD). Nothing transfers automatically to other machines; see [Status / Roadmap](#status--roadmap).

- **Full boot up to Boletaria**: logos, auto-save notice, intro with sound and subtitles, title screen, character creation, tutorial, the Nexus, and Boletarian Palace 1-1.
- **D3D12 rendering**: lit/textured scenes, shadows, HUD, particles, light shafts and haze; shaders are precompiled (the game does not pause to compile shaders).
- **Save/load works**, including menu sounds.
- **60 FPS runtime profile**, approved via runtime identity verification. On the reference machine, 1-1 with `DES_FPS=60` and `VBLANK_MHZ=240000` measured ~120 FPS — not a general claim.
- **Startup audio-silence bug fixed at the root**: a libmixer notify-queue race, documented in `docs/34-audio-notify-startup-race.md`, fixed by `patches/des-audio-notify-fix.patch`, validated by A/B runs (PCM reaches WASAPI with the fix ON and does not with it OFF; 3752/3752 and 8442/8442 notifications across two test sets) and audibly validated by the author on an isolated installation.
- **Launcher (Alpha 0.2)**: imports/prepares/repairs a managed installation from your local ISO or `PS3_GAME` copy, enforces identity gates (unapproved runtimes are blocked), records every operation as JSONL traces. On an intact installation, full verify takes ~4 s reading **0 bytes** (NTFS/USN identity confirmation); re-preparing from the same ISO takes ~9 s.
- **Architecture**: complete PPU boot, recompiled SPU code images, SPURS HLE with task coordination, cellAudio/cellAtrac/cellFs/camera/input reimplementations, in-game UI via RmlUi.

## Status / Roadmap

**Current state: Alpha 0.2 — experimental prototype.** Validated on one reference machine only. There are no guarantees about stability, performance, or compatibility on any other machine.

**In development** (open items):

- Transparency effects that vanish for single frames (fog wall, soul aura, snow).
- Cutscene handling above 30 Hz.
- Physics validation above 60 FPS.
- More controllers (keyboard+mouse and DualSense USB/BT-HID are what have been touched; other pads untested).
- Persistent user configuration.

**Not implemented / known limits:**

- Online/network: nothing implemented.
- Only the first area is verified — the rest of the game is untested.
- Full SPURS/SPU LLE: an earlier experiment was abandoned (`docs/22-libsre-lle-attempt.md`); the runtime uses HLE plus recompiled SPU images.
- Cutscenes revert to 30 Hz above 30; physics > 60 FPS untested (the original game runs at 30).

Project history and technical chronicle: `docs/00-feasibility.md` through `docs/35-launcher-prepare-optimization.md`.

## Requirements

### To play (prebuilt launcher package)

- Windows 10/11 x64; a GPU with D3D12 feature level 12.0+.
- The self-contained launcher package from [Releases](https://github.com/Leonardorobertomotalvao/demons-souls-pc/releases/latest) — no .NET install needed.
- **Your own legal copy of the game** (PS3 USA, BLUS30443), and a decrypted `EBOOT.ELF` produced **from your own copy** — e.g. the executable dump feature of RPCS3, or `scetool` with public keys. The project never decrypts anything for you and never ships decryption.
- Disk space for the managed installation (a copy of the game data plus vfs and profile).

### To build from source

- Windows 10/11 x64; VS 2022 Build Tools (C++ + Windows SDK with `d3d12.h`), CMake ≥ 3.20, Ninja, and the clang used by the fork.
- Python 3.11+ with `ps3recomp/requirements.txt` (pycryptodome, capstone, construct, tabulate, tqdm) for the analysis tooling.
- .NET SDK 8 (launcher).
- RPCS3 (or scetool with public lv0/metldr keys) to decrypt **your own copy** — the project distributes no decryption and no keys.
- Ghidra optional (analysis).

## Installation

### End users (prebuilt launcher)

1. Download `DemonSoulsPC-Launcher-Windows-x64-<version>.zip` from [Releases](https://github.com/Leonardorobertomotalvao/demons-souls-pc/releases/latest) and extract the **whole** zip into a writable folder, keeping the `Launcher\` folder together.
2. Run `Launcher\DemonsSouls.Launcher.exe`.
3. **SELECT LOCAL ISO** — the launcher mounts your game image read-only, identifies the edition by `PARAM.SFO` (BLUS30443 · 01.00), and fills the source path. If Windows refuses the mount, double-click the ISO in Explorer and try again.
4. You must also supply: an **approved** `des_boot.exe` runtime and the `EBOOT.ELF` from your own decrypted copy (recommended route: RPCS3's executable dump). The launcher does **not** decrypt protected `EBOOT.BIN` files; without an approved ELF identity, PLAY stays locked.
5. Click **PREPARE** (or **REPAIR**) — it copies and verifies everything into a managed installation (resumable by SHA-256; your original `PS3_GAME` folder is never modified).
6. Under **Advanced**, confirm the paths, pick 60 FPS, and click **PLAY** — every start verifies the runtime and EBOOT SHA-256 before releasing the button.
7. For isolated testing, set `DEMONS_SOULS_LAUNCHER_HOME` to a scratch folder. The launcher never touches its own preferences.

### From source

The launcher has a single reproducible build:

```powershell
git clone https://github.com/Leonardorobertomotalvao/demons-souls-pc demons-souls-pc
cd demons-souls-pc
dotnet publish launcher-alpha02-hotfix\DemonsSouls.Launcher.csproj -c Release -r win-x64 --self-contained true -o ..\launcher-build
# → ..\launcher-build\DemonsSouls.Launcher.exe
```

The runtime is **not** a one-button build: clone the [ps3recomp](https://github.com/JonathanDC64/ps3recomp) fork at commit `add3cac07e29b4457ab24303804bfcc96056ba5a`, apply `patches/des-audio-notify-fix.patch`, set up MSVC (`scripts/vsenv.bat`), place your own decrypted game files in `work\game\` (per `docs/03-setup.md`), and build the CMake targets (port + runtime) for x64 → `port\build\des_boot.exe`.

**Honesty about reproducibility:** on the author's toolchain the audio-fix build reproduces `83B23813254C542575244F394ED31A3BE2BBD276E6A1D70372D2C6C12A77AE7E` byte-exactly; other toolchains may differ in bytes. The launcher approves runtimes by identity (SHA-256) — to run your own build, reproduce your installation's own gates.

## Usage

### Launcher

Open `DemonsSouls.Launcher.exe` → the **Installation** tab wizard shows every requirement in real time (image/source, prepared `EBOOT.ELF`, approved native executable, destination, disk space) → **PREPARE / REPAIR** → **Advanced** paths → 60 FPS → **PLAY**.

### Command line (reproducible, no GUI)

```text
--fps-compatibility --installation <dir> --profile <p> --runtime <exe> --elf <ELF>   → JSON verdict (approved / blocker)
--prepare-iso --iso <file.iso> --destination <dir> [--elf <ELF>] [--runtime <exe>]   → exit codes 2/3/4 name the exact missing requirement
--verify --installation <dir>                                                        → full integrity check
--import, --repair, --detect-tools, --self-test, --window-test
```

`--prepare-iso` also accepts `--source-image` to reuse the previously registered ISO identity (seconds instead of minutes on repeat runs).

### Approved runtime identities

The launcher refuses unapproved runtimes ("60 FPS blocked — check the paths under Advanced and try again"):

| Build | SHA-256 | Status |
|---|---|---|
| Alpha 0.2 stable | `88087725D7CB858B644D8D183329EB72E1C820B365ED2B34EBD6E2AC281F0933` | Approved |
| Alpha 0.2 audio fix | `83B23813254C542575244F394ED31A3BE2BBD276E6A1D70372D2C6C12A77AE7E` | Approved (audibly validated by the author) |
| Any other `des_boot.exe` | — | **Blocked by design** |

### Diagnostics (common symptoms)

| Symptom | Likely cause | Where to look |
|---|---|---|
| "60 FPS blocked" | runtime under Advanced is not an approved build | identity table above; `docs/34` |
| "The selected folder must contain installation.json" | pointed at the parent folder | point at the manifest subfolder, or run PREPARE |
| Total silence at boot (`[audio-peak] peak=0.0000`) | libmixer race (fixed since Alpha 0.2-audiofix) | `docs/34`; A/B via `scripts/audio-ab-diagnostic.ps1` |
| Native executable SHA mismatch | installation manifest points at an old runtime | REPAIR with an approved runtime |
| Where are the logs | — | `%LOCALAPPDATA%\DemonsSoulsPCLauncher\logs\` |

## FAQ

**Is this an emulator?**
No. Static recompilation translates the game's machine code ahead-of-time, once, into native x64 code. There is no general-purpose CPU emulation at runtime — PS3 system libraries are reimplemented natively, and rendering/audio go through Direct3D 12/WASAPI.

**Does the download include the game?**
No. The Releases page contains only the launcher and tooling — no game code, no game data, no firmware, no keys. You must supply your own legally-obtained copy, and the launcher verifies identities before letting you play.

**Where do I get the game?**
You don't get it from here. This project requires a copy of the PS3 game that you legally own; you decrypt its own EBOOT locally on your machine (RPCS3 executable dump or scetool with public keys). The project distributes no decryption and no keys.

**Does it really run at 60 FPS? The original is 30.**
There is an approved 60 FPS profile, gated by runtime identity. On the reference machine, area 1-1 measured ~120 FPS with specific settings — that measurement does not transfer to other machines, and physics above 60 FPS is untested.

**Why is my `des_boot.exe` blocked?**
The launcher enforces identity gates: only builds whose SHA-256 is approved run with 60 FPS. This is deliberate — it prevents running unknown or tampered executables. See the identity table above.

**Can I contribute a build of the runtime?**
Runtime builds are personal (built from your own decrypted copy); you can't attach them to an issue. Patches, port code, launcher code, scripts and docs are welcome — see [Contributing](#contributing).

**Is this affiliated with Sony, FromSoftware or Atlus?**
No. It is an independent, unofficial fan project with no affiliation, sponsorship, or endorsement from any rights holder. See the [Legal disclaimer](#legal-disclaimer).

## Screenshots

<!-- Screenshot policy: this repository deliberately contains no game-derived artwork
     (see Legal disclaimer). Safe to commit: screenshots of the launcher's own UI
     (your own software), placed under docs/screenshots/ or .github/assets/.
     If you want in-game captures, host them externally and link them here by URL,
     or consciously change the project's stated policy first. -->

When available, they will appear here:

<!-- <p align="center">
  <img src="docs/screenshots/launcher-installation-tab.png"
       alt="Installation tab of the Demon's Souls PC launcher, showing the prepare wizard with source ISO, approved runtime and destination checks passing" width="700">
</p>
<p align="center">
  <img src="docs/screenshots/launcher-play-60fps.png"
       alt="Launcher advanced tab with an approved des_boot.exe and EBOOT.ELF paths confirmed and the 60 FPS toggle selected before PLAY" width="700">
</p> -->

## Contributing

PRs are welcome for `port/`, launcher sources, patches on `ps3recomp`, scripts, docs, and tests. House rules (details in [CONTRIBUTING.md](CONTRIBUTING.md) and `AGENTS.md`):

- One technical problem per PR; changes small, tested, and reversible.
- **Absolutely nothing game-derived**: no `work/`, `.elf`, `.self`, `.prx`, dumps, saves, keys, firmware, assets, or game screenshots — never.
- Describe real evidence (environment, date, hashes) in every PR; don't label anything "validated" that wasn't.

Bug reports and feature requests: use the issue templates ([bug report](https://github.com/Leonardorobertomotalvao/demons-souls-pc/issues/new?template=bug_report.yml) · [feature request](https://github.com/Leonardorobertomotalvao/demons-souls-pc/issues/new?template=feature_request.yml)); open questions and setup help go to [Discussions](https://github.com/Leonardorobertomotalvao/demons-souls-pc/discussions).

## Legal disclaimer

- **This is an unofficial fan project** — a research prototype. No warranty of stability, performance, or compatibility of any kind.
- **No game content is included or distributed here.** The repository ships only original code and tooling. The game's machine code and data stay yours: you decrypt them locally, on your machine, for interoperability, from your own copy. Keys, dumps, and assets are never committed, published, or shared.
- **Trademark notice:** "Demon's Souls" is a trademark of Sony Interactive Entertainment. This project is an independent, unofficial fan port, with no affiliation with or endorsement by Sony Interactive Entertainment, FromSoftware, Atlus, or other rights holders. No artwork, logos, or game content appear in this repository.
- You are solely responsible for your copy of the game. The project's rules are defined in `docs/05-legal.md`.
- **License:** this project's code is MIT ([LICENSE](LICENSE)). Upstream ps3recomp: MIT. .NET 8 and RmlUi: MIT with standard notices. Fonts: OFL (Alegreya Sans, Cinzel) and Apache 2.0 (Material Symbols), with license files included in `port/ui/res/fonts/`.

## Links

- **Website:** https://leonardorobertomotalvao.github.io/demons-souls-pc/
- **Website source:** [`docs/`](docs/) — static site (HTML + CSS + minimal JS, no build step), served by GitHub Pages from `main` + `/docs`. Its SEO metadata (meta tags, sitemap, JSON-LD, hreflang) improves discoverability; search engines alone decide indexing and ranking — no guarantee of indexing or positions.
- **Repository:** https://github.com/Leonardorobertomotalvao/demons-souls-pc
- **Releases (launcher packages):** https://github.com/Leonardorobertomotalvao/demons-souls-pc/releases
- **Discussions:** https://github.com/Leonardorobertomotalvao/demons-souls-pc/discussions
- **Issues:** https://github.com/Leonardorobertomotalvao/demons-souls-pc/issues
- **Changelog:** [CHANGELOG.md](CHANGELOG.md)
- **Technical chronicle:** [`docs/`](docs/) — 35 documents from feasibility to the launcher's prepare-optimization
- **Upstream tooling:** [ps3recomp](https://github.com/sp00nznet/ps3recomp) (MIT) · fork [JonathanDC64/ps3recomp](https://github.com/JonathanDC64/ps3recomp) (MIT)
