# D CPU on-foot vertical slice (standalone prototype, not a full game release)

This PR is intentionally stacked on P0 validation PR #109, which itself targets A validation #107. All new/changed files belong to D. Root `scenes/main.tscn`, `project.godot`, Python and original saves are unchanged. **A alone integrates/merges.**

## Preview command

Open `res://scenes/rework/world/cpu_vertical_slice.tscn` explicitly in Godot 4.7.2, then **Run Current Scene (F6)**; do not run the project's legacy main scene. The isolated scene defaults to `demo_mode=true` and **does NOT persist real XP/rewards**; the in-memory ACK is only for preview. Movement: WASD or arrow keys, press F near a marker/terminal, auto-firing follows the mouse aim. Default 1280×720 orthographic XZ, 50% look-ahead. Progress by walking:
1. Hub -> dispatch terminal -> motherboard.
2. Defeat the three weak intro enemies via cursor auto-fire to unlock hardware gates.
3. Walk to CPU gate and press F (other four gates intentionally report unsupported in this CPU-first slice).
4. Explore a seeded maze of real static wall/floor colliders. Find/activate CPU secure_core and stop_intruders goals using F.
5. Remain near secure_core for a provisional 2400 physics ticks. Kill 10 wave enemies after activating stop_intruders. Objective progress is driven by actual fixed ticks / B damage-confirmed kills, not UI.
6. Boss only spawns once both objective tracks complete. Defeat it using B damage math and mouse-based auto-fire.
7. CPU cleanse unlocks extraction marker. Walk to it, press F; preview mode acknowledges a **fake, non-durable** E receipt and returns to the hub.

The B combat bridge is intentionally a temporary D test harness that calls B `Stats`, `Damage` and `Events` directly, with minimal chasing/contact behavior and projectiles. **Actual production enemy AI, three skills, mana, procs, balance and combat VFX remain B/A work.** No D-owned combat rules replace B's calculation. Enemy stats/boss HP, intro count, objective durations and map sizes are **[draft v0.1]**; 8–12 minute hardware pacing is NOT verified.

## A/E durable integration API (mandatory before release)

- `vertical_slice_demo.gd.demo_mode=false` for production (A owns the entry point). A calls `attach_progression_bridge(EBridge)` before `confirm_mission_started(run_id,seed)`. The run must already have been saved by E via `begin_mission(seed)`; D will not write save data or issue a second start transaction.
- Hub `terminal_requested({schema:1,type:"hub_terminal_requested",terminal_id,action})` handles GUI on A's side, dispatch action emits `mission_start_requested({seed,...})`. A/E acknowledges with `confirm_mission_started`. Plug-in DB/vaccine/skills/status terminals are real 3D interaction points with no unauthorized persistent mutation.
- On kill, B's adapter must emit stable `enemy_defeated(actor_id, receipt_id)` **after actual damage reaches HP zero**. D validates requested actor IDs and deduplicates objective progress. Intro enemy kill IDs also drive gate lock. `cpu_objective_session.gd` publishes spawn metadata `enemy_spawn_requested({run_id,region_id,encounter_id,actor_id,role,position,boss,seed,difficulty,environment})`; A/B replace temporary enemy presentation/AI as needed.
- `world_fact_requested` publishes D objective/clean receipts for E. If A injects `progression_bridge` with `handle_world_fact`, it receives those synchronously; otherwise A must use `acknowledge_world_fact(receipt_id, {ok:true})` after E durable transaction success. Failed E write freezes preview/controller; never silently rewards.
- Extraction uses `ExtractionGate.prepare(region)` to build stable receipt without consuming the D run. It **cannot finalize until all objective and cleanse facts are acknowledged**. `extraction_commit_requested(fact)` emits when no E bridge is attached; A calls `acknowledge_extraction({ok:true,receipt_id})` only upon successful durable E commit, then D `request_extraction` marks final and returns hub. Lost/failed ACK blocks transition; A must reconcile E receipts and reattach before retry.
- Death uses the same durable ACK-before-local-end rule through `death_commit_requested` / `acknowledge_death`; E's 3-clone/4th-death/lifetime fence policy is never simulated as persistence by D.
- The preview's future GPU/RAM/SSD/HDD gate transitions are NOT implemented; only the gate models/data and isolated draft objectives exist.

## QA

Run after import:
```sh
godot --headless --path . --script res://tests/rework/world/cpu_vertical_slice_smoke.gd
```
A owns CI workflows and Windows manual QA. Verify mouse aim during camera lag/resize/DPI 100-200%, maze wall collision, keyboard/movement, 10 confirmed kills and 40s core defense, boss, extraction lock and durable E bridge, failure/restart handling, 8–12 minute pacing with measured gameplay.
