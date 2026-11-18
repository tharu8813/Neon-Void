# Neon Void · Godot 4 top-down 3D edition

## 실행 모드 (2026-11-18)

**`main`의 기본 실행은 이제 버전 선택 화면(`scenes/launcher.tscn`)이야.** 기존 4개 OS 구역 Godot 게임은 삭제하지 않았고 선택 메뉴의 **기존 Godot 게임**에서 실행할 수 있어. Python/Tk 게임은 그대로 [`python/`](python/)에 보존되어 있어.

- **새 리워크 플레이:** `scenes/rework/integration/rework_main.tscn`. 일반 백신 성장·5개 하드웨어·E 영구 저장을 사용해. 별도의 `nv-rework-main-v1` 슬롯을 사용하며 테스트용 스킬이나 성장 포인트를 자동 지급하지 않아. 일반 플레이의 스킬 획득·보스 패턴·연출 개선은 [#143](https://github.com/tharu8813/Neon-Void/issues/143)에서 진행 중이며, 현재 파일럿에는 **초안** 스킬 9종·결정적 3장 초안·보스 패턴·절차적 사운드가 들어가 있어. 스킬 풀과 수치는 아직 `[미확정]`/BALANCE_DRAFT라 승인된 최종 밸런스가 아니야.
- **새 리워크 QA:** `scenes/rework/integration/rework_qa_boot.tscn`. 테스트용 스킬 3종, 능력치 포인트 5개, 복제 3회 등이 제공되는 **독립 QA 슬롯**을 사용해. 실제 일반 플레이 보상이나 획득 방식과 동일한 시작 조건이 아니야.
- **기존 Godot 게임:** `scenes/main.tscn`. 이전 화면·게임플레이·저장 시스템을 그대로 유지해.
- **기존 Python 게임:** `python/`. 별도 실행, 저장 파일 자동 이관 없음.

**Windows Actions에는 세 종류의 실행 파일이 있어:** `NeonVoid-MAIN-launcher-windows` (메인 선택 화면), `NeonVoid-REWORK-QA-windows` (즉시 QA 시작), `NeonVoid3D-legacy-windows-export-smoke` (기존 Godot). 일반적으로 첫 번째 파일을 받아 실행하면 돼.

**주의:** 3개 게임 모드의 저장 데이터는 자동으로 합쳐지지 않아. 기존 저장 파일을 지우거나 덮어쓰지 않아. 원본과 달라진 신작 리워크 기능이 모두 완성되었다는 뜻도 아니야.

This is the playable Godot 4.7.2 **top-down 3D migration** of the existing Python/Tk Neon Void, not a redesign. The Python edition is preserved in [`python/`](python/), including `python/neonvoid/`, `python/neon_void.py`, its independent tests and packaging. Original player save JSONs at the repository root remain at the same location for compatibility. The 3D port preserves the original 1280×720 logical coordinates and 60Hz gameplay, 17 original enemy silhouettes, all four OS sectors × five waves, software, XP, antivirus database, extraction and legacy-save import.

**Current status (2026-11-17):** Windows test reported successful startup, WASD/aim/dash, deploy/CPU combat, Q/E, software selection/automatic waves and real Task Manager GPU 3D activity. Audio output was **not tested** due to the user's environment. New original-faithful ambient/FX animation remains subject to real Windows visual QA. Automated CI and headless EXE boot do not establish frame quality or audio acceptance.

## Run

- Pinned editor: [Godot 4.7.2 stable](https://godotengine.org/download/archive/4.7.2-stable/), standard GDScript build, no .NET.
- Open `project.godot` in the editor, or from the repository root:

```powershell
godot --editor --path .
godot --path .
```

- Windows test build: repository **Actions → Godot 4 port checks → Windows x64 export and executable headless boot → Artifacts → NeonVoid3D-windows-export-smoke**. Artifacts expire after seven days. Extract and run `NeonVoid3D.exe`; the export has its `.pck` embedded and does not need Python.
- The original Python edition is still available: `cd python` then `python neon_void.py`, `python -m neonvoid` or `run.bat`. It continues to use the **original repository-root save location** for source runs; user data is never auto-moved.

## Controls and game flow

Spawn at the walkable antivirus base, approach **OS DEPLOYMENT (F)**, deploy into the OS hub and select a sector gate via movement + F. Each zone contains five waves and an independent boss. Clearing all four zones enables extraction to permanently keep one software module. The next OS inherits the antivirus DB's persistent growth.

| Input | Action |
|---|---|
| WASD / arrow keys | Move, fixed logical collision |
| Mouse | Aim and automatic fire, neon game cursor |
| Space / Shift | Dash in movement direction |
| F | Nearby terminal / sector gate / arena left EXIT |
| Q, E | Pulse / shield, with mana and cooldown |
| 1, 2, 3 / click | Choose one of three offered software cards |
| Tab, O, Esc | Loadout, settings, pause |
| M, F11, Alt+Enter | Mute, fullscreen, borderless |

## Legacy visual identity

Python `view.py` and `game.py` remain the visual design reference. The Godot port preserves the minimal neon-on-dark art direction, pulsing 3D polygon player/viruses, 52 twinkling stars and 80px arena grid, 34 ambient OS dust motes, conduit data packets, moving scanline, 14-silhouette dash-afterimage pool, 380-particle hit/kill/boss burst budget, wave intro/sweep/flash, source hitstop, boss ring/telegraph, critical/chain/explosion effects. All procedural decorations are **read-only**; the deterministic combat simulator owns the original mechanics. Reduced-effects mode hides cosmetic motion but preserves vital warnings.

## Safety and QA

The app never auto-imports or overwrites Python saves. Back up `save.json`, `settings.json`, `profile.json`, `antivirus_db.json` and `os_checkpoint.json` and test imports with **copies only**. Use the Godot in-world import flow; existing legacy originals must remain byte-identical.

- `tests/`: 4×5 integrations, controller/combat, visuals, HUD, recovery/atomic persistence and lossless migration.
- `docs/WINDOWS_FINAL_ACCEPTANCE.md`: GPU/frametime/DPI/audio and long-run human acceptance protocol.
- `docs/P0_FINAL_CODE_PARITY_AUDIT.md`: original source-to-Godot code parity and known differences.
- `docs/P9_LEGACY_ATMOSPHERE_PARITY.md`: procedural atmosphere and effects visual acceptance.

The Windows export is **not yet a signed release installer**. Audio, multi-monitor/DPI comparison, 30–60 minute full clear and performance p95/p99 remain acceptance work, not silently passed.
