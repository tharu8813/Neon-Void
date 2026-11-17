# Parallel Godot integration contract (P1, draft PR #65)

Reference: Epic #52, P0 #53, P1 #54 and [frozen gameplay baseline](../docs/GODOT_MIGRATION_BASELINE.md).
The original Python commit is `245663ba15149cc1262304b966062989c24c13f6`.
**This is a proposed integration agreement. It does not claim teams' adapters are implemented.**
PR #65 is Draft, and dependent merges require the prerequisite issue gates.

## Global invariants

- Godot **4.7.2 stable**, GDScript, 60 fixed physics ticks/s. Keep Python/Tk playable until P10 release approval.
- Logical legacy frame: **1280x720, X right/Y down**, simulated in Godot **XZ**. World scale **0.01** unit/legacy px is currently provisional. A strictly vertical orthographic camera must preserve the old view. P2 alone calibrates pixel projection, DPI, letterboxing, hub scrolling and mouse ray inversion against physical captures.
- Preserve movement/dash, auto-aim, deterministic combat, damage, four sectors, 5 waves per sector, boss conditions, thirty original software IDs, infection, extraction exactly one permanent software and XP receipt idempotency. Reference numeric Python implementation wins over engine defaults.
- Original `save.json`, `settings.json`, `profile.json`, `antivirus_db.json`, `os_checkpoint.json` remain immutable. Only E/P8 can propose **explicit opt-in, copy-first** migration after backups, version checks and rollback tests.
- Replay uses gameplay seed 42, simulated dt=1/60 s. Never let cosmetics/particles consume the gameplay RNG.

## Existing common runtime API (already in PR #65)

`RuntimeConfig` is the sole global autoload from `res://scripts/runtime_config.gd`. Other teams **must not edit this file** without A coordination.

| API | Contract |
|---|---|
| `LOGICAL_WIDTH`, `LOGICAL_HEIGHT` | 1280, 720 integer logical pixels |
| `TICK_HZ`, `WORLD_UNITS_PER_PIXEL` | 60, 0.01 |
| `logical_to_world(point: Vector2, elevation: float = 0.0) -> Vector3` | (px.x*S, elevation, px.y*S) |
| `world_to_logical(point: Vector3) -> Vector2` | Inverse XZ transform |

P1 root is `res://scenes/main.tscn` Node3D `Main`, with `Camera3D`, `ArenaFloor` and `PlayerVisual` primitive placeholders. These and `res://scripts/main.gd` are **owned by A**. They are not playable or parity-verified.

## Exclusive file ownership (proposed for B/C/D/E to acknowledge)

| Team | Issue scope | Owned new paths |
|---|---|---|
| A | #53 #54 #62 #63 | `godot/project.godot`, `godot/scenes/main.tscn`, `godot/scripts/{main,runtime_config}.gd`, `godot/visuals/**`, `godot/fx/**`, `godot/benchmarks/**`, `godot/TEAM_CONTRACT.md`, `godot/tests/port_smoke.gd`, Godot workflow and QA docs |
| B | #55 P2, #56 P3 | `godot/scripts/{camera,player}/**`, `godot/scenes/{camera,player}/**`, `godot/tests/{camera,player}/**` |
| C | #57 P4, #58 P5 | `godot/scripts/{combat,world}/**`, `godot/scenes/{combat,world}/**`, `godot/tests/{combat,world}/**` |
| D | #59 P6, #60 P7 | `godot/scripts/{software,ui}/**`, `godot/scenes/{software,ui}/**`, `godot/tests/{software,ui}/**` |
| E | #61 P8 | `godot/scripts/persistence/**`, `godot/tests/persistence/**` and synthetic migration fixtures only |

If another team needs to edit a path owned by someone else, propose an API on the owner PR; A performs a later integration commit. No silent shared-file changes, force pushes, or modifications to frozen Python logic. Existing branches take precedence; announce conflicts on Epic #52.

## Planned seams (these are **design proposals**, not callable APIs yet)

- B/P2 will publish `screen_to_logical(viewport_px: Vector2)` and `logical_to_screen(logical_px: Vector2)` that use identical letterboxing, distinguish clicks outside the visible scene, and support 4:3 and 21:9. B/P3 reports read-only movement/aim/dash snapshots in legacy logical units.
- C/P4 consumes B snapshots and owns combat RNG, hit resolution, enemy state, and damage outcomes; C/P5 owns sector/wave transitions and world state, not UI rendering.
- D/P6 owns stable software ID to stat modifier mapping; D/P7 reads state snapshots and emits intent commands instead of changing state directly.
- E/P8 accepts explicit serializable state snapshots, validates schema/version/idempotency before writing new **Godot-only** data; legacy files never change.
- A/P9 consumes visual event snapshots only; mesh/FX animation cannot change 60Hz gameplay state, timing, collision radius or RNG.

Each team's PR MUST name the proposed public node path, callable function or signal with Godot type/unit, lifecycle owner, consumer, fixture test and intended integration point. Do not traverse private child nodes of another team. Interfaces become binding after their own reviewed implementation, not merely by appearing here.

## Validation + merge gates

- One issue/PR per stage; branch off the latest compatible base and declare dependencies.
- Current-HEAD Python Windows/Linux and Godot Windows/Linux headless must succeed; Godot PR additionally must pass Windows x64 export+exported EXE headless boot. This does **not** certify GPU, DPI, visuals, or 30-60 minute gameplay.
- Stage merges respect each issue's prerequisites. In particular #65 cannot merge until P0 physical Windows visual/DPI reference and P1 physical Windows render QA, even if all automated jobs pass.
- Coordinator verifies PR changed paths, mergeability, all latest-head checks, reviews, API compatibility and regression scope. Squash merge with expected HEAD SHA only after every gate. Record integration changes and cross-team blockers on #52.

Status format for each PR and issue comment: `HEAD`, `base`, `changed files`, `API change`, `consumers`, `CI result`, `physical Windows QA evidence/pending`, `merge blocker`.
