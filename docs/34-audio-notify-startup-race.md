# 34 — Startup race in libmixer audio notifications (total silence) — root cause, fix and validation

Date: 2026-10-22 · Affected runtime: Alpha 0.2 (`DES_FPS=60` profile) · Status: fixed in the `fix/audio-launcher-publication` branch; human listening validation still pending.

## Symptom

The Alpha 0.2 runtime renders a fully silent picture: WASAPI opens cleanly (187.5 blocks/s, 0 dropped frames, endpoint buffer 1920 frames), but `[audio-peak] peak=0.0000` from the first to the last block and the mixer read ring for every port stays `[........]` — the game never writes PCM into the cellAudio ports.

## Root cause (confirmed by local A/B and by hash-verified binary instrumentation)

Demon's Souls' libmixer child publishes an event-queue key (`0x8000CAFE02460300`) into its shared state **after** the parent has already read that state and called `SetNotifyEventQueue`:

1. The parent reads `state+0x500` while it is still `0`.
2. The parent calls `SetNotifyEventQueue(0x0)`. The HLE wrapper succeeds and the parent keeps the key `0` as its argument.
3. The child publishes the key and creates a keyed queue, but nobody delivers `CELL_AUDIO_EVENT_MIX` to it.
4. The title's audio thread sleeps forever in `sys_event_queue_receive`; the port rings never fill; silence.

Instrumentation of the failing runs printed `stale key=0` for **every** possible early start; with the fix, the log line `stale key=0 published=0x8000CAFE02460300 queue=16 waited=1ms` shows the same call resolving 1 ms after the parent's failed attempt.

## The fix (`patches/des-audio-notify-fix.patch`)

Minimal HLE recovery, scoped to the single verified call site:

- NID `0x377E0CD9` in `ps3recomp/runtime/ppu/ppu_hle.cpp`: a handler guarded by exact
  caller identity (`lr == 0x01546380`, TOC == `0x019D3BAC`, `r30 == 0x01CE3680`) re-reads
  `state+0x500` up to 250×`Sleep(1)` until the published key resolves to a live queue id
  (`state+0x524`); only then does it replace the stale `0` argument. After 250 ms it gives
  up and keeps the original path (same behavior as before).
- `DES_AUDIO_NOTIFY_FIX=1` is the default; `DES_AUDIO_NOTIFY_FIX=0` restores the old
  behavior for A/B testing. **The launcher strips DES_* environment variables before
  launching, so the fix is on by default in normal play and failure-safe.**
- Diagnostic telemetry (`AUDIO_STAGES`, `AUDIO_FLOW`, `AUDIO_NEXUS_TRACE`,
  `[audio-notify]`, `[audio-stage]`, `[audio-device]`, `[audio-decode]`) is added in
  `cellAudio.c`/`cellAtrac.c`; none of it is active by default and none of it performs
  file I/O on the mixer thread.
- `cellAudio.c` `audio_notify_event_queues` snapshots the notify slots under the same
  mutex used by registration, removing a pre-existing data race the A/B telemetry
  exposed (a partially registered slot could otherwise be observed by the mixer).

The patch applies to [ps3recomp](https://github.com/JonathanDC64/ps3recomp) at commit `add3cac07e29b4457ab24303804bfcc96056ba5a`; `git apply` the file inside the `ps3recomp/` folder (paths are `a/ps3recomp/...`).

Risks considered before shipping:

- **No new waits on steady state.** The bounded wait only exists on the one NID call
  whose argument is `0` with the verified caller identity.
- **No guessing.** The recovery requires the published key to resolve to the *child's
  own* queue id; a wrong-queue delivery is impossible by construction.
- **Fallback correct.** Timeout keeps the original argument, matching pre-fix behavior.
- **Telemetry off by default**; no PCM drops, no perf regressions seen in A/B
  (187.4–187.8 blocks/s before and after; device buffer steady at 1920).

## Automated validation (performed on this Windows 10 machine; console speakers unused)

Each run ~110 s using the diagnostic runner (isolated profile, `AUDIO_PEAK/RATE/GAPS=1`), same `launch.json`, only `DES_AUDIO_NOTIFY_FIX` toggled:

| Run | Fix | Result |
|---|---|---|
| `fixon-20261022-091438-fa99e4e3` | ON | `[audio-notify-fix]` fires once at start; ring fills; `silent 0.0%` sustained from the 3rd rate window on; WASAPI peak up to 0.61; 0 frames dropped |
| `fixoff-20261022-091657-6a5c7a22` | OFF | Exact original symptom sustained: 8 rate windows at `silent 100.0%`, ring `[........]`, peak 0.0000 |
| `fixon-stages-20261022-091914-ae47601f` | ON + `AUDIO_STAGES=1` | `[audio-notify] delivered=8442/8442 unresolved=0 push_failed=0`; port 1 `raw_nonzero=938/938`; `[audio-device]` 0 get/release failures; `[audio-decode]` PCM non-zero, `short=0 bad=0` |

Same result independently reached in an earlier A/B (20–21 Oct) on build
`audio-fix-build-v2` (sha `06a6841d66e68185a315953e68c6ec88d6ede9dbd8648565580f53c83222dd62`):
3752/3752 notifications delivered with the fix on, 0/3752 with it off.

## What is still pending

- **Human listening validation** (menu music, combat SFX, death/respawn loops) — automated A/B proves
  PCM is flowing into WASAPI, it does not prove it sounds right. Do not publish a
  distribution until this is done.
- The published Alpha 0.2 runtime (`Release\Alpha02-60FPS\Game\des_boot.exe`,
  sha `88087725…`) stays untouched; the fixed build is a separate artifact
  (`port\build\des_boot.exe`, sha `83b23813…`, PowerShell: see launcher docs).

## Launcher changes shipped together with this fix

- `Installation.GameRoot` now also resolves a selection that contains `vfs\PS3_GAME`
  (the layout of a launcher-prepared installation); the strict game-root checks
  (PARAM.SFO title/version, EBOOT.BIN, mandatory USRDIR folders) are unchanged.
- PREPARAR/REPARAR with an empty Advanced field now reports the missing field in
  Portuguese instead of `ArgumentException("path")` from `Path.GetFullPath`.
- The 60 FPS runtime identity gate accepts both approved builds (original Alpha 0.2 and
  the audio-fix build). The manifest hash of a managed installation still gates
  non-explicit runtime selection, so repairing with a wrong exe stays rejected.

## PREPARAR/REPARAR: verified step-by-step (Windows CLI of `launcher-alpha02-hotfix`)

What the import writes (from `Installation.Import`/`ImportCore`, lines 1285–1438):
**only inside the destination**: `vfs/PS3_GAME/**`, `runtime/EBOOT.ELF`,
`runtime/des_boot.exe` and `installation.json` (plus `.partial`/`.corrupt-*`
temporaries owned by the same copy). The source is only read. New destinations are
created; an existing destination is accepted only if it already carries `installation.json`
of the *same source and inventory* — anything else is rejected ("Escolha novo destino"),
so point it at a fresh folder.

Checks performed before and during the copy: game root validated (PARAM.SFO
BLUS30443/01.00, EBOOT.BIN, USRDIR layout), ELF/runtime non-empty and validated,
source≠destination (never overlapping), no junction/symlink traversal, no ADS paths,
destination outside any `.git`, enough free space (2× transfer + 256 MB reserve),
a per-destination lock file, no `des_boot.exe` running from that destination, SHA-256
of every source file computed **before** copying, and the temporary file re-hashed
before the atomic rename. An interrupted import resumes against the saved plan
instead of restarting.

Exact command used to build the audio-fix test installation:

```bat
set DEMONS_SOULS_LAUNCHER_HOME=C:\Users\Helio\AppData\Local\Temp\opencode\launcher-home-test
DemonsSouls.Launcher.exe --import ^
  --source "F:\DemonSouls-PC\GameData\vfs\PS3_GAME" ^
  --destination "F:\DemonSouls-PC\AudioFixTest" ^
  --elf "F:\DemonSouls-PC\GameData\runtime\EBOOT.ELF" ^
  --runtime "F:\DemonSouls-PC\DemonsSoulsRecomp\port\build\des_boot.exe"
```

Result (2026-10-22): EXIT=0; 12522 files, 8,77 GB copied; `installation.json`
`Complete=True Synthetic=False`; `runtime/des_boot.exe` sha256 `83b23813…`,
`runtime/EBOOT.ELF` sha256 `90eaa5e9…`. `--fps-compatibility` against this
installation reports `approved=true blocker=null` with the default (copied) runtime —
this is exactly the state the JOGAR button's checks evaluate.

Repeat the same command with `--destination "F:\DemonSouls-PC\GameData"` to migrate the
managed installation after human listening validation; its recorded origin
(`D:\PS3_GAME`) differs from this test source, so a careless hit is rejected rather
than overwriting production data.

## A/B evidence on the test installation (audio)

Replaying the launcher's own `launch.json` of the test installation, ~110 s per run,
only `DES_AUDIO_NOTIFY_FIX` toggled (logs under
`%TEMP%\opencode\diag-audiofixtest\<tag>-<timestamp>\`):

- `fixon-test` (boot won the timing race benignly — no stale key, no fix needed):
  silence falls to a sustained 0.0% with WASAPI peaks up to 0.57, 0 dropped frames/s.
- `fixoff-test`: `[audio-notify-fix]` absent and the game sustains **silence 100.0%, peak 0.0000**
  for all 9000 blocks — the exact Alpha 0.2 symptom. The startup race manifests on a
  per-boot basis (thread scheduling decides who wins; the OFF boot lost it).
- `fixon-test2` (boot lost the timing race): `[audio-notify-fix] stale key=0
  published=0x8000CAFE02460300 queue=16 waited=1ms initial=0x0` — the 250 ms-bounded
  recovery resolved in 1 ms and the run went to a sustained 0.0% silence, 0 dropped
  frames/s.

Together with the three earlier runs on the developer machine this establishes:
with the fix OFF and the race present the game is silent; with the fix ON the game
reaches the mixer either through the fix or through the benign ordering, and both
deliver PCM.

Known cosmetic launcher CLI issue (not fixed in this branch): `--play-test` prints a
`DirectoryNotFoundException` looking for `<installation>\tests\run-*` after starting
the game — the launcher actually creates the run under `<profile>\tests\run-*`. The
game process is already started at that point and is unaffected; the GUI's JOGAR
button does not use this listing path.

---

**UPDATE 2026-10-22 (after this document was written):** the pending human listening
validation was completed — the author validated the fixed runtime
(`83B23813254C542575244F394ED31A3BE2BBD276E6A1D70372D2C6C12A77AE7E`) audibly on the
isolated test installation (startup and in-game audio confirmed OK). The validation
status above ("human listening validation still pending") therefore no longer applies
to the author's environment; mechanical A/B evidence in this document remains the
reproduction recipe for other setups. Original text preserved as written.
