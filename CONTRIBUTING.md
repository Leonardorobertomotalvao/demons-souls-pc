# Contributing

Thanks for considering a contribution to Demon's Souls PC — an unofficial, experimental fan port. To keep this project healthy and strictly legal, please read these short rules before opening a pull request.

## The one absolute rule

**Nothing game-derived, ever.** Do not commit or link:

- Game files of any kind: `work/`, `.elf`, `.self`, `.prx`, ISOs, dumps, saves
- Firmware, BIOS, keys, or decryption material
- Game assets or in-game screenshots

This repository ships only original code and tooling. See `docs/05-legal.md` for the full policy — the maintainer has the final word on keeping game material out.

## House rules for PRs

1. **One technical problem per PR.** Small, tested, reversible changes.
2. **Real evidence in every PR**: environment (hardware/OS/toolchain), date, and results (hashes, logs, measurements). Don't label anything "validated" that wasn't validated.
3. **Honest claims.** State explicitly what you tested and what you didn't. This project documents its own limits (see README §Status / Roadmap); PRs should do the same.
4. Follow the existing code style of the area you touch (`port/`, `launcher-*`, `patches/`, `scripts/`, `docs/`).
5. Check `AGENTS.md` for repo-wide conventions.

## What PRs are welcome for

- `port/` — the port layer (runtime code, UI, heap, stubs)
- `launcher-alpha02-hotfix/` — the current launcher source
- `patches/` — patches applied on the MIT-licensed upstream [ps3recomp](https://github.com/JonathanDC64/ps3recomp)
- `scripts/`, `tools/`, `docs/`, tests

## Testing expectations

- Launcher changes: run the embedded self-tests (`--self-test`) and, when relevant, the CLI flows (`--fps-compatibility`, `--verify`, `--prepare-iso`).
- Runtime changes: describe your build environment and how you exercised the change. The project cannot gate other people's runtime binaries (identity gates exist for exactly that reason), so describe your validation instead.
- Audio changes: A/B with `scripts/audio-ab-diagnostic.ps1` (`DES_AUDIO_NOTIFY_FIX=0/1`) where applicable, plus a listening test.

## Issues

- **Bug reports** and **feature requests**: use the issue templates.
- **Questions, setup help, compatibility reports**: open a thread in [Discussions](https://github.com/Leonardorobertomotalvao/demons-souls-pc/discussions) instead of an issue.

## Licensing

By contributing, you agree that your contributions are licensed under the same MIT license that covers this repository ([LICENSE](LICENSE)).
