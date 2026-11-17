# Rework world/camera, D-owned v0.1

Branch: `rework/d-world-foundation-20261117`. Ref: #98, parent #95.
The playable `scenes/main.tscn`, legacy campaign and all saves stay untouched.
The new contract is **proposed**, and A must approve integration signatures.

## Entry points

- `mission_planner.gd` -> `create_mission(seed: int, previous_mutation_missions: int = 0) -> Dictionary`. Output schema 1 contains ordered five-region snapshots, difficulty `easy|normal|hard`, one guaranteed easy, environment `normal|mutation|stack_overflow|tunnel_infection|spread|initial`, `mutation_stage` 0 or 1..5, independently derived `map_seed` and `encounter_seed`. Region ordering does not affect random streams.
- `hardware_catalog.gd` -> `get_hardware(id)`, `objectives(id)`. **[draft v0.1]** per-hardware objectives, enemy roles and boss roles. B is responsible for making these executable encounters.
- `procedural_map.gd` -> `generate(region_id, map_seed, environment) -> Dictionary`, `distance_field(map,start)`, `all_floor_reachable(map)`. Maps contain `tiles` (PackedInt32Array, row major: 0 wall/1 walkable), `width`, `height`, `spawn`, `boss`, `extraction` (Vector2i grid), `objectives` (array of id/grid/world), `world_bounds` (Rect2 XZ in world units). `cell_to_world` maps a cell to XZ meters. Maze backbone provides connectivity. Pathfinding/collision volumes, enemies and combat are NOT wired.
- `extraction_state.gd` -> instance `Extraction.new(run_id)`. `confirm_objective(region_id,objective_id,receipt_id)`, `confirm_boss_defeated(region_id,receipt_id)`, `is_extraction_open(region_id)`, `request_extraction(region_id)`, `report_death()`, `snapshot()`. State changes are one-shot and duplicate receipts/objectives are ignored. **Do not use this as a persistence engine**.

## Proposed event contracts (E/B/A)

`world_fact(Dictionary)` for `objective_completed`, `boss_defeated`, `hardware_cleansed`. Minimum keys: `schema: 1, type, run_id, region_id, objective_id, receipt_id`.

`extraction_committed(Dictionary)` exactly once per in-memory run, on `mission_extracted` or `mission_failed`; success adds `outcome: success, cleared_regions: Array[String], cleared_count: 1..5, full_clear: bool`, death uses `outcome: death`. `receipt_id: world:<run_id>:extraction|death`. **E must enforce persistent idempotency/atomic rewards and resolve mid-save failure**. D never changes XP, clones, database, slots or original JSON. B/A must only call confirmation APIs after actually validated combat/objectives (do not trust client/UI events).

`look_ahead_math.gd` -> `window_to_logical(pointer_px,client_size_px)`, `camera_target(player_xz,logical_cursor,bounds,view_height,max_distance,margin)`, `pointer_ground(current_camera_xz,logical_cursor,view_height)`, `aim_direction(player_world,current_camera_xz,logical_cursor,view_height)`, `smooth(...)`. `look_ahead_camera.gd` provides `follow(player_world,cursor_logical,delta_seconds)` and `pointer_ground_from_window(pixel,client_size)`. **A must sample actual rendered camera position for shooting**; NEVER aim at the camera's upcoming target, which would introduce cursor feedback. XZ in world units, nominal 1280x720 logical canvas, 7.2 world-unit vertical FOV and Y-up. Input and client sizes must use the same physical/logical pixel units. The scene has no automatic integration hooks.

## Pending decisions and QA

- [Unconfirmed] Re-enter/teleport to cleared hardware, coexisting environment effects, bonus scale, goal count, enemy/boss specifics, map geometry, extraction commit boundary. Current single-environment and two-objective layouts are adjustable prototypes, not approved game rules.
- Environment `tunnel_infection` must exclude learned plugins. B/C/E own actual plugin selection/modification and visible UI warnings.
- New mission means new seeds (E/A provide run ID and seed). No global RNG or rendering RNG may influence world/combat seeds.
- No claim of 8-12 minutes per hardware before end-to-end Windows play.
- D tests: `godot --headless --path . --script res://tests/rework/world/world_foundation_smoke.gd` and `godot --headless --path . --script res://tests/rework/camera/look_ahead_smoke.gd`. A owns workflows and must explicitly wire them to Linux/Windows CI. Regular headless import alone does not execute these two tests.
- Windows QA: playable navigation/collision and runtime pathfinding, extraction portal location and unlock, 16:9/4:3/ultrawide/high-DPI/zoom aim alignment, edges/corners/resize, smoothness at variable frame rates, GPU performance and 8-12 min pacing.

The optional `scenes/rework/world/procedural_preview.tscn` only visualizes the generated map. It is NOT a playable hub/mission/encounter.
