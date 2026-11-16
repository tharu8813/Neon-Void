# Godot 4 port: frozen Python gameplay baseline (P0 / #53)

> **Reference commit (immutable):** `245663ba15149cc1262304b966062989c24c13f6` (2026-11-17 KST). This is a **parity contract**, not evidence that the Godot port exists. Python/Tk remains runnable until release acceptance.

## Source-of-truth and drift policy

- **Reference:** the commit above, plus `neonvoid/game.py`, `view.py`, `entities.py`, `controls.py`, `combat.py`, `content/{stages,sectors,enemies,upgrades,virus_catalog}.py`, `world/session.py`, `infection.py`, `extraction.py`, `metaprogression.py`, `os_checkpoint.py`, and the existing tests.
- The pinned legacy behavior takes precedence over design sketches, older historical documentation, and Godot engine defaults. Rebalance or interaction changes require a separate reviewed issue.
- `godot/` is an additive project; **never delete or silently rewrite** Python/Tk during migration. A Godot milestone must not modify original JSON in place.
- Fixed timestep `DT=1/60` sec; gameplay uses 1280×720 logical pixel coordinates with screen-space Y down. Render frame rate and physics tick rate are distinct. Tk loop clamps elapsed wall time to 0.15 sec and catches up at most 3 simulation steps per callback, dropping remaining accumulator afterward; preserve simulation rules but document any deliberate scheduling differences.

## Coordinates, camera, pointer, resolutions

- Reference logical plane: `W=1280, H=720`; initial arena player at `(640,360)`, hub world start `(1200,780)`, world size `2400×1560`.
- Arena uses screen-space logical positions. Hub camera top-left from `camera_for(x,y)`: `clamp(x-640, 0,1120)`, `clamp(y-360,0,840)`. Camera scrolling must not change collision or movement logic.
- Godot 3D proposal for P2: choose an explicit world-unit scale `S` (e.g. 0.01 m per legacy pixel); map `(x,y) -> Vector3(S*x, 0, S*y)` and aim/move within XZ, while Camera3D is vertically overhead, orthographic and oriented so positive source Y appears downward on screen. This proposal is **not yet a validated exact transform**; P2 must calibrate projected pixels and Y direction against captures.
- Fit content by `scale=min(window_width/1280,window_height/720)`; left/top letterbox are `(window - logical_size*scale)/2`. Pointer input must invert this SAME transform; input in the bars is not in-world input. Hud/terminal hitboxes share the transform.
- Visual reference matrix: `960×540` (0.75), `1280×720` (1.0), `1920×1080` (1.5), and `1024×768` letterbox (0.8, left=0, top=96). For Windows, capture at 100%, 125%, 150%, and 200% OS DPI where supported; compare title, in-world hub, arena battle, menus, pointer, HUD, software selection and terminal screens. **Screenshots and physical DPI tests are P0 manual pending, not claimed by automated unit tests.**
- Avoid camera angle tilt, perspective, zoom-to-fit shortcuts that change silhouette/aim, game-space Y inversion, collision rules or world bounds.

## Player, controls, projectiles and simulation contract

- Player defaults: HP 100, radius 13, movement max desired speed 322 px/s, attack damage 10, auto-fire rate 5.4 shots/s, projectile speed 880 px/s, armor 4, armor penetration 0, critical chance 0.05, MP 100, mana regen 6/s, pickup radius 82. See the complete `Player.st` dictionary in `entities.py`.
- Normal movement input is normalized for diagonals; velocity smooths toward desired movement with `k=clamp(9*dt,0,1)`, then collides against the original boundaries and hub pillars.
- Dash: default one charge, recharge 1.05 simulated seconds, active for 0.145 s, dash movement velocity 940 px/s and 0.32 s invulnerability on activation. Dash direction priority: **current movement input -> velocity (if magnitude >20) -> last movement -> up**; never mouse aim. Shift always works; space is the default configurable binding. Dash collision/ability effects stay legacy.
- Auto-attack uses pointer-relative aim `atan2(mouse_y-player_y,mouse_x-player_x)`; player_shoot timer resets to `1 / max(0.8, rate)`. Distinguish auto fire from hold-to-shoot input for special modes.
- Q: pulse skill, E: guard skill; F world interaction, P pause, Tab software inventory, O settings, M sound mute, R manual abandon/restart where allowed, Escape pause/back, F11 fullscreen, Alt+Enter borderless; arrow keys movement fallback. All nine rebindable defaults are in `controls.py`; reserved `M/R/O` and uniqueness cannot be weakened.
- Armor formula `damage*100/(100+max(0,armor-max(0,penetration)))`; DoT bypasses armor and cannot recursively trigger on-hit effects. Preserve max DoT stacks, status time behavior, crit, piercing, ricochet, homing, lifesteal, enemies and boss AI. No Godot physics material or rigidbody defaults may change hit rules.
- Fixed seed for test reproducibility: **42** (e.g. `python -m neonvoid --selftest 3600 --no-sound --seed 42 --god`). Gameplay `Game.rng=random.Random(seed)` and separate global FX RNG are both seeded; sector wave planning is keyed by sector/wave/seed. GUI screenshots need a controlled input script, not only a seed.

## World-state / UI parity inventory

| System | Required preserved behavior and reference |
|---|---|
| Hub | Player controls immediately in world; no pre-deployment list UI; `OS DEPLOYMENT`, `ANTIVIRUS DATABASE`, `DIAGNOSTICS`, `SETTINGS` terminals |
| OS junction | All four sector gates available in any order; `SYSTEM STATUS` extraction terminal and `PURIFICATION STATION`, physical F interactions |
| Sectors | CPU CORE (`overseer`), MEMORY SECTOR (`replicator`), STORAGE VAULT (`encryptor`), NETWORK GRID (`zero_day`) |
| Waves | Five waves per sector, final wave demands boss **and all hostiles** dead. Retreat retries incomplete wave, preserves completed waves and run resources, no repeated reward |
| Infection | Player infection thresholds at 25/50/75%, OS integrity, clean stations and partial cleansing; no input debuffs; integrity failure emergency exit |
| Combat content | Legacy classic stage plus specialized virus archetypes, boss phases, collision, auto-fire, Q/E skills, armor/DoT/penetration. Enemy stats are `E_PARAMS` |
| Software | Exactly 30 stable IDs with rarity, caps, selection, in-run effect and per-sector reward weighting; never swap IDs to translated UI names |
| Permanent growth | Lifetime XP, level cap 30, XP threshold `100+50*(level-1)`, base-stat bonuses per level `(+2 HP,+0.2 damage,+0.15 armor,+1 MP)`; distinct one-software extraction receipt |
| Campaign finish | OS CLEAN only once all four sectors clear. Player **chooses exactly one eligible software** on extraction; new OS does not inherit all modules; XP receipts are idempotent |
| Checkpoint | Safe-boundary resume under same run ID; completed waves, resources, infection and UI choice persist. Interrupted combat wave restarts; live enemies/bullets not serialized |
| HUD/UI | Title, pause, dead, software offer, equipped-software inventory, settings/keybindings, profile, database, extraction/salvage, checkpoint continuation prompt, boss/infection HUD, pointer animation |
| Audio/display | Existing SFX/music semantics, cursor visibility, user-configurable effects, fullscreen/borderless/windowed and letterboxing. Actual DPI/audio/latency require Windows manual QA |

### Sector and software content IDs

- Sectors: `CPU`, `MEMORY`, `STORAGE`, `NETWORK` (game may use lowercase aliases for world/DB IDs).
- Software (order preserved): `rapid, power, pierce, split, homing, boom, crit, vamp, thorn, dash2, dashb, slow, magnet, drone, chain, rear, hull, ricochet, armor, breach, mana, regen, burn, toxin, leech, focus, cool, eco, sustain, duration`.
- Enemy catalog: `drifter, mini, shooter, splitter, rusher, boss, overheater, worm, trojan, ransomware, rootkit, packet_swarm, guardian, overseer, replicator, encryptor, zero_day`.
- Exact numeric values and unlock conditions MUST continue to be derived from referenced Python tables. A mere ID-count check is insufficient for full parity; see tests and manual scenario checklist.

## User data / migration boundaries

| File | Legacy contract | Godot rule |
|---|---|---|
| `save.json` | high score, best wave, runs | read-only source, preserve all original bytes |
| `settings.json` | schema v5, bindings + window, volume and effects | preserve unknown/future versions without replacement |
| `profile.json` | schema v2, recent runs, summary stats | don't overwrite legacy history |
| `antivirus_db.json` | schema v1, XP receipts, archived software and closed runs | **authoritative** durable reward source; exact-once semantics |
| `os_checkpoint.json` | schema v1, revision, run ID, safe-boundary progress | continue same run; never duplicate XP or archived software |

- Unfrozen Python stores files beside source; Windows frozen EXE uses `%LOCALAPPDATA%\\NeonVoid`. Godot must not assume a shared directory or silently auto-migrate between them. P8 must specify path discovery and explicit user opt-in.
- `tests/fixtures/godot_migration/` holds **entirely synthetic examples**, not snapshots of real user data. Fixtures are read only; all import/round-trip validation writes to disposable temporary folders.
- On migration: validate schema first, hash + back up original files, convert to a **separate** Godot destination, validate counts/receipts, and atomically switch only after success. Invalid/future schemas block migration with an actionable error. Do not consume or rewrite a legacy file on read. Explicit rollback must restore the original game without changed data.
- Files with missing/default data and corrupted/future-version data have different semantics. Never treat a corrupted database as a fresh DB and then persist it.

## Fixed-seed acceptance scenarios for later Godot stages

1. **Baseline selftest:** `python -m neonvoid --selftest 3600 --no-sound --seed 42 --god` both runners. Check exit 0, runtime trace and state; this is NOT a visual QA substitute.
2. **Input/camera:** with 42, compare 960×540/1280×720/1920×1080 windowed + letterboxed 1024×768, cardinal/diagonal movement, stationary and moving dash, cursor hits and aiming center/edges. Check logical-to-screen-to-logical round trip.
3. **Wave control:** 42, enter CPU, clear wave 1; leave mid wave 2, re-enter and confirm wave 2 restarts with no duplicate wave 1 reward. Repeat final-wave boss and support kill gating.
4. **All sectors:** 42, clear each 1–5 in freely chosen order, compare wave patterns, 4 unique bosses, cleansing, statuses, 30 software IDs and eligible extraction offers.
5. **Persistence:** use only disposable fixture copies. Restart a checkpoint at CPU 2/5 + MEMORY 1/5, compare run ID, resources and receipts. Verify duplicate grant/extract rejection and corrupt/future JSON fail-closed behavior.
6. **Windows experience:** at 100–200% DPI and available resolutions, check cursor, terminal hitboxes, audio, 30–60 min play and CPU/GPU/p95 frametime. Record screenshots and a comparison grid before final P0 sign-off.

## Automated reference evidence and remaining gaps

- Frozen main push `245663ba`: [Python validation run #43965199038](https://github.com/tharu8813/Neon-Void/actions/runs/43965199038): **success**, 2026-11-16 21:57 UTC; includes Windows/Linux unit and GUI smoke suites + fixed seed 3600-step selftest.
- Same main: [Windows standalone build run #43965199101](https://github.com/tharu8813/Neon-Void/actions/runs/43965199101): **success**; Windows onefile build + 360-frame selftest. CI success proves automated check completion, not real-game visual quality.
- Parity guard test: `python -m unittest tests.test_godot_migration_baseline -v`; fixture import validation uses temporary paths only.
- **Not complete yet:** pinned visual screenshots on physical Windows, all OS-DPI permutations, human gameplay control-feel/audio check, actual graphics comparison and multi-hour sustained FPS/GPU profiling. Those are manual gates; do not mark them passed based on CI.
