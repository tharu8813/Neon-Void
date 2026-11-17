# B / Production CPU combat integration, revision 1

**Scope:** issue #96; implementation follows B #110, assembled against A's **review-only** #115 snapshot. All new code and tests live in B-owned `scripts/rework/combat/` and `tests/rework/combat/`. This document is the D/C/A handoff, not authorization to mutate D's world or A's global scripts.

## D: replace the temporary bridge, no API rename

When A/D is ready to wire the actual playable scene, the only *required* change in D's `scripts/rework/world/vertical_slice_demo.gd` is its existing class import:

```gdscript
# OLD (D temporary)
const CombatPreview = preload("res://scripts/rework/world/demo_combat_bridge.gd")
# NEW (B production, after review)
const CombatPreview = preload("res://scripts/rework/combat/production_cpu_adapter.gd")
```

The new script **extends B's existing `combat_node_bridge.gd`**, wraps `combat_runtime.gd` and implements D's original:
- `begin_encounter(player_node: CharacterBody3D, camera_node: Camera3D, map_plan: Dictionary, run_id: String) -> void`
- `spawn_hostile({actor_id,position,boss?,max_health?,move_speed?}) -> bool`
- `reset() -> void`, `enemies: Dictionary` keyed by stable actor ID, and `player_stats: Dictionary` for observation
- Signals: `enemy_defeated(actor_id,validated_receipt)`, `player_died()`, `health_updated(health,max_health)`
- Debug-only `_apply_hit(enemy_id,hit_key)` retained solely for D's existing scene smoke; normal gameplay relies on B swept projectiles and the unique collision-hit ledger. Never use this debug entry point to award real progression.
- Exact enemy receipt remains `<run_id>:enemy:<actor_id>:death`, emitted once on the first verified B HP death; E/A still enforce exactly-once award. B never saves, awards XP or trusts UI-only death facts.

### Additional A-facing methods

- `configure_plugins(levels: Dictionary, seed: int) -> bool`: supply the **E-authorized** plugin level map *after a durable mission start / card claim*. No automatic read/write to saves. `upgrade_plugin_after_ack(id, level) -> bool` only after E's confirmed progression transaction.
- `learn_active_skill(definition: Dictionary, slot: int, progression_authorized: bool) -> bool`: B validates mana/cooldown/slots, A/E validate learned skill ownership. No slot can bypass the authorization flag.
- `set_combat_input(command: Dictionary)`: optional per-tick command from A input layer. `move: Vector2` is XZ, `aim_world: Vector3` comes from camera's DPI-correct world conversion, `fire: bool` requests automatic firing, `dash: bool` uses movement direction, `skill_slot: int` 0–2. Default provisional key sampling: WASD/arrow move; SPACE dash; Q/E/C slots, not a final remapping policy.
- `advance_production_tick() -> Array[Dictionary]`: explicit *single* 1/60s simulation step for tests/A tick synchronization. `_physics_process` handles up to eight catch-up ticks at once **without discarding** the remainder.
- `diagnostics() -> Dictionary`: counters, number of enemies/projectiles, FIFO proc metrics, pending C events and C fault reason.
- Additional signals: `mana_updated(mana,maximum)`, `plugin_failure(reason)`, inherited `combat_notice(note)` and `combat_actor_died(id,killer_id)`.

### Lifecycle and physics

Each `begin_encounter` resets and registers `VACCINE` and a new deterministic seed/encounter ID. `reset` removes B-created enemy nodes and projectile visuals, returns control of the existing D player and its movement toggle to the original owner, and clears C state/queuing, dedup ledgers and input. It does **not** queue_free the player/camera or touch world objects, scene root, save slots or `project.godot`.

The D player motor remains the physics movement owner (no double-drive); B applies movement-direction dash via `CharacterBody3D.move_and_collide` and disables D motor input while B root/stop control is active. Enemy movement runs B chase/range AI with D-provided `Maps.distance_field` guidance around CPU walls, then `move_and_slide` before publishing physics-corrected positions back into B. Walls clip projectile swept segments via D `Maps.is_floor` tile samples before B actor-hit queries; B's hit ledger prevents a pierce from harming the same target twice. It is still a CPU tile-grid/planar combat adapter, not a general-purpose NavMesh, 3D height collision solver, or physics-verified laser line of sight.

Do not edit the D-owned files in this B PR. Only A/D switches the preload in their reviewed integration PR; until that step the opt-in CPU scene keeps its existing demo behavior. The existing default legacy scene is untouched.

## C: actual plugin effect execution

`c_effect_adapter.gd` uses **C-owned** `plugin_effect_runtime.gd` (event eligibility, 27 effect definitions, ICD/counters, deterministic RNG) and C's `plugin_b_combat_sink.gd`. It does not duplicate C recipes. It supplies snapshots of actual B actors/positions and injects a transactional sink:
1. Translate B FIFO events to C `basic_attack, on_hit, on_crit, on_hit_proc, damage_dealt, damage_taken, kill, skill_cast, skill_damage, dash, tick`. For C execute, use `derived_attack` before B's `Damage.calculate` (not a second post-hit attack). Attach stable run-scoped event ID, actor/target, per-attack tags, proc ancestry and permission flags.
2. C `process(event, world, sink)` computes an atomic command batch. B calls C `Sink.apply` on a *copy* of the live actor snapshot. If C sink rejects, neither C counters/ICD nor B HP/status are committed; `plugin_failure` exposes the reason. No enemy progression receipts are fabricated.
3. Commit approved HP/DoT/status results to the same B `combat_runtime.actors`, then emit exactly one normal `hit`/first `death`. C-generated `on_hit_proc` events are **queued**, not recursively invoked; they retain C `proc_path` and can continue legally across targets without permitting exact repeated plugin+actor+target cycles.
4. New C `projectiles` commands create actual B projectiles with inherited ancestry, hit-proc permissions and efficiency; `projectile_transform` supports draft laser piercing and boomerang return; `homing` uses a configurable numeric turn rate. C buffs are applied as short-lived stat offsets, `shield` is stored as a provisional shield-first pool, and status stacks use B `Statuses.apply`. All on-hit and omni-vamp overlap semantics remain governed by B's existing provisional policy.

**Limitations / stop conditions requiring C/A agreement:** C's current chain-lightning sink rejects `missing_adjacent_chain_target` when a lone victim is hit, even though an empty chain should be harmless; B emits `plugin_failure` instead of inventing C cooldown behavior. C's `rare_reward_request` requires a separate **E receipt** and is explicitly rejected here until A/E connects that transaction. Laser/boomerang visuals, wall occlusion for area/chain effects, homing turn-rate units, shield damage ordering and complex projectile shapes are provisional and require manual gameplay review. Plugin effects do not write saves, reward random rares, or silently select draft balance values.

## Test commands and merge order

The B-owned `tests/rework/combat/production_cpu_adapter_cases.gd` is preloaded by the existing B `combat_foundation_smoke.gd`, which A's CI executes in **Linux + Windows** from the combined PR. The test hot-swaps the B adapter into an actual instantiated D `cpu_vertical_slice.tscn` **in memory only**, confirming D's intro enemies, stable kill receipts, motherboard gate and CPU transition. Separate C contract tests use the real C definitions and B HP/status application. Previous 60Hz seeded B regression remains unchanged.

```sh
godot --headless --path . --script res://tests/rework/combat/combat_foundation_smoke.gd
```

A remains sole owner of merge order: #100 → #110 → this B follow-up, with separate C and D owner PRs and A scene wiring. The PR targeting A's #115 is a **stacked review artifact, not an instruction to merge the composite**. Require all newest-head CI checks and manual Windows DPI/aim/dash/scene/FPS/audio/GPU and clean-save checks before release.
