# Neon Void · Godot 4 top-down 3D edition

This is the playable Godot 4.7.2 **top-down 3D migration** of the existing Python/Tk Neon Void, not a redesign. The `neonvoid/` and `neon_void.py` Python original remain intact. The 3D port preserves the original 1280×720 logical coordinates and 60Hz gameplay, 17 original enemy silhouettes, all four OS sectors × five waves, software, XP, antivirus database, extraction and legacy-save import.

**Current status (2026-11-17):** Windows test reported successful startup, WASD/aim/dash, deploy/CPU combat, Q/E, software selection/automatic waves and real Task Manager GPU 3D activity. Audio output was **not tested** due to the user's environment. New original-faithful ambient/FX animation remains subject to real Windows visual QA. Automated CI and headless EXE boot do not establish frame quality or audio acceptance.

## Run

- Pinned editor: [Godot 4.7.2 stable](https://godotengine.org/download/archive/4.7.2-stable/), standard GDScript build, no .NET.
- Open `godot/project.godot` in the editor, or from the repository root:

```powershell
godot --editor --path godot
godot --path godot
```

- Windows test build: repository **Actions → Godot 4 port checks → Windows x64 export and executable headless boot → Artifacts → NeonVoid3D-windows-export-smoke**. Artifacts expire after seven days. Extract and run `NeonVoid3D.exe`; the export has its `.pck` embedded and does not need Python.
- The original Python edition is still available as `python neon_void.py` or `run.bat`.

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

- `godot/tests/`: 4×5 integrations, controller/combat, visuals, HUD, recovery/atomic persistence and lossless migration.
- `godot/docs/WINDOWS_FINAL_ACCEPTANCE.md`: GPU/frametime/DPI/audio and long-run human acceptance protocol.
- `godot/docs/P0_FINAL_CODE_PARITY_AUDIT.md`: original source-to-Godot code parity and known differences.
- `godot/docs/P9_LEGACY_ATMOSPHERE_PARITY.md`: procedural atmosphere and effects visual acceptance.

The Windows export is **not yet a signed release installer**. Audio, multi-monitor/DPI comparison, 30–60 minute full clear and performance p95/p99 remain acceptance work, not silently passed.
