# B / Combat v1.0 provisional integration contract

Source: GDD v1.0 §§6–8,11 and [rework issue #96](https://github.com/tharu8813/Neon-Void/issues/96).
**This is a proposed API for A/C/D/E review, not an approved cross-team contract.**
All modules extend `RefCounted`, are detached from legacy scenes and run at a caller-controlled fixed 60 Hz. No autoloads, rendering calls, saves, GPU RNG, input bindings or gameplay scene mutations.

## Files / integration calls

- `stats.gd`: `Stats.make(overrides)`, one shared player/enemy dictionary shape. Rate fields (`crit_chance`, `tenacity`, `physical_pen_percent`, `omni_vamp`) are fractions, not 0–100 percentages.
- `damage.gd`: `Damage.calculate(attacker_stats, defender_stats, attack, critical=false)` returns raw/mitigated figures; `Damage.apply_damage(defender, result, invulnerable)` updates HP only; `Damage.vamp_components(attacker, attack, actual_damage)` returns **separate**, not automatically stacked healing amounts.
- `statuses.gd`: `Statuses.apply(state, definition)`, `active_flags(state)`, `stat_offsets(state)`, `tick(state)`. Statuses are authored as data with `stack_mode` one of refresh/replace/stack/independent. `tick` produces DoT damage requests and HP regeneration requests; apply through damage system, never directly through VFX.
- `events.gd`: `Events.new()`, `set_tick(tick)`, `submit_root(payload)`, `submit_proc(parent_event, effect_id, payload, icd_ticks)`, `drain(handler)`. `handler(event) -> Array[{effect_id, event, icd_ticks}]`. Source root must carry stable `hit_key` (projectile/attack collision identifier) and `target_id` to prevent duplicate hits. Root proc path is empty; children extend signature `effect_id|actor_id|target_id`. Same effect on **different actor/target pairs** is allowed; repeating the same effect/source/target triple inside the same ancestry is rejected; independent attack roots have fresh ancestries. Per-effect/target ICD is caller-supplied in ticks. `allowed_proc_triggers` on parent permits only listed child `trigger_kind` values.
- `../skills/skill_slots.gd`: `configure(stats)`, `learn(definition)`, `equip(slot,id,progression_authorized)`, `tick()`, `cast(slot,at_tick,haste,flags)`, `dash(movement_vector,at_tick,dash_definition,flags)`. E/A owns authorization and learned-skill storage. Zero-based slot indexes 0,1,2 only.

## Proposed events for C / A

```json
{
  "event_id": "120:15",
  "tick": 120,
  "kind": "on_hit",
  "actor_id": "vaccine-1",
  "target_id": "virus-7",
  "attack_id": "basic",
  "damage_type": "physical",
  "critical": false,
  "source_tags": ["basic_attack"],
  "hit_key": "projectile-33",
  "proc_path": [],
  "allowed_proc_triggers": ["on_hit", "on_crit"]
}
```

D/A supplies `run_id/encounter_id/seed` and stable entity/collision IDs; B creates deterministic combat event IDs. C supplies `effect_id`, `trigger_kind`, `source_tags`, `icd_ticks` and downstream proc permission lists in effect payloads. E owns learned skill IDs, authorization to equip, and serialized progress. **No team should use generated IDs as persistent reward receipts**: A/E must supply durable run/receipt IDs for transactions.

## Rules status and unresolved decisions

**[확정]** 3 active slots plus separate movement dash, 120% critical base multiplier, distinct physical/magic/true damage, nonzero cooldown, AoE or DoT omni-vamp 33% (both together still 33%), multi-step proc chains without repeated effect/target ancestry cycles.

**[초안 v0.1]** Defaults other than GDD-confirmed AD=5/AP=0/projectile speed=1/projectile size=10/crit multiplier=1.2; physical/magic resistance curve, percent-then-flat penetration, haste formula, multiplicative tenacity × boss CC resistance, 60 Hz DoT sampling and zero-negative haste clamp.

**[미확정]** On-hit plus omni-vamp total healing/ordering, shield or overkill leech, exact boss immunity and CC stacking, skill/dash cooldowns and bindings, attack-speed cap, malfunction probability, true damage vs shield, status DOT damage type, gameplay-damage rounding and target-specific refresh/stack conditions. A/C should review before integration. Current `apply_damage` uses HP-only clamp as a prototype, not a final shield rule.

## Test commands (Godot 4.7.2)

```sh
godot --headless --path . --script res://tests/rework/combat/combat_foundation_smoke.gd
godot --headless --path . --script res://tests/rework/skills/skill_foundation_smoke.gd
```

A owns `.github/workflows/**`: add these commands to **both Linux and Windows** jobs after import and preserve failure exit status. Existing legacy Godot smoke tests do not imply these new tests executed. Manual Windows QA still required for movement input, skill mapping, projectile collision and 60 Hz frame pacing after integration.

## Additional status action gate
`Statuses.action_allowed(state, action, gameplay_rng)` applies stop/root/silence flags and data-authored malfunction attack failure. Pass a **dedicated, seeded gameplay RNG**, not a VFX RNG. `Statuses.modified_stats(base, state)` derives ephemeral combat stats without altering permanent allocations. The max-of-sources malfunction chance is **[초안 v0.1]**, subject to balance review. Infinite projectile piercing is a status flag (`infinite_pierce`) consumed by A's projectile collision integration.

### Collision lifetime and memory contract
Every projectile or attack occurrence uses a globally unique `hit_key` for that encounter, shared across all its collision callbacks. At the **end** of that projectile's lifetime, A/D calls `Events.retire_hit_key(hit_key)` so the duplicate-hit ledger can be dropped. Do not retire while the projectile is capable of further hits. Events' processed-ID scratch bookkeeping is cleared after draining; metrics remain available for profiling. Per-target ICD state remains valid across ticks until `reset()` at the next encounter.

## P1 runtime / C-D-A bridge (stacked follow-up PR)

`combat_runtime.gd` is the **B-owned deterministic 60 Hz combat source of truth**. It is not automatically loaded by the legacy scene. `combat_node_bridge.gd` is a Node3D that **A must explicitly attach to the rework scene**, and whose input and projectile visuals are supplied by A. The fallback XZ plane does **not** implement D terrain/wall collision or Godot physics by itself. Attach actual `CharacterBody3D`s, set `drive_actor_nodes=true` for bridge-driven movement or leave false for A-driven actors, then pass physics-corrected `global_position` back each tick. Do not double-drive externally controlled bodies.

### B runtime API
- `Runtime.new(seed, {encounter_id, grid_cell_size, event_work_per_tick, vamp_overlap_policy, default_projectile_speed, projectile_radius_scale})` then `register_actor(id, team, Vector3 worldXZ, stats, options)`. Actor options include `player_controlled`, `radius`, `attack_definition`, `projectile_speed`, `projectile_pierce`, `projectile_lifetime_ticks`, `attack_range`, `preferred_min_range/max_range`, `dash_definition`. Default physical basic attack is 100% AD [초안]. Units: 3D world units/s, 60 ticks/s, fractional percentages.
- `set_actor_plugins(id, instances)`, `learn_skill(id, def, slot, progression_authorized)`, `set_actor_position(id, Vector3)`, `step_fixed({actor_id:{move:Vector2 XZ,aim_world:Vector3,fire:bool,dash:bool,skill_slot:0..2}})`, `snapshot(id)`, `projectile_snapshots()`.
- Output `step_fixed` is an array of **presentation-only** notices: `projectile_spawn`, `projectile_retire`, `hit`, `death`, `skill_cast`, `dash`, `vamp_policy_pending`. They are not persisted receipts.
- `combat_node_bridge.gd`: `setup(encounter_id,seed,config)`, `register_node(actor_id,team,Node3D,stats,opts)`, `set_input(id,command)`, `set_projectile_factory(Callable)`, `advance_one_tick()`. Signals `combat_notice`, `combat_actor_died`. It is opt-in and scene-free so no existing root changes. D supplies enemy IDs + positions/geometry, A supplies mouse-to-XZ ray/keyboard/scene graphics.

### Exact C integration contract (proposed, requires review)
C's current catalog has `plugin_id`, `trigger.event`, `trigger.source_tags`, `proc_permissions.allowed_proc_triggers`, `internal_cooldown_seconds`, `cooldown_scope`, level and Korean descriptions. This **does not yet contain executable effect numbers/commands**. B does **not** infer numbers from localized summaries. C or A supplies an instance preserving those fields plus **versioned numeric**:
```json
{
 "plugin_id": "example_lightning", "level": 1,
 "trigger": {"event": "projectile_hit", "source_tags": ["basic_attack"]},
 "internal_cooldown_seconds": 3, "cooldown_scope": "per_target",
 "proc_permissions": {"allowed_proc_triggers": ["on_hit"], "can_trigger_on_hit_effects": true},
 "effect_commands": [
  {"kind": "damage", "target_policy": "nearest_other_enemy",
   "attack": {"damage_type":"magic", "base_damage": 12, "ad_ratio":0, "ap_ratio":0.2, "can_crit":false}}
 ]
}
```
This is a **sample API payload**, not the confirmed content or an automatic change to C's 27 plugins. Recognized commands: `damage`, `status`, `heal`, `stat_modifier`, `spawn_projectile`; `passive_stat_modifiers` is a numeric additive map, `trigger_after_hits` is mandatory for `on_hit_proc_count` (no guessing from descriptions). `status` command supplies an explicit `status` dictionary; `stat_modifier` supplies `stats,duration_ticks,stack_mode`. `target_policy` currently supports `current`, `self`, `nearest_other_enemy`. C `trigger.event` mapping: `projectile_hit`, `damage_dealt`, `skill_damage`, `skill_cast`, `on_hit_proc_count`, `health_threshold`, `periodic`, `mobility_or_reveal` to B runtime `on_hit/skill_cast/damage_taken/periodic/dash` fields. Unsupported triggers remain inert, not silently executed. Every chain child carries `proc_path`, source/target IDs, trigger kind, tags and C permission data. Per-target/actor/global ICD keys differ from cycle path keys. A's implementation must explicitly test the C catalog once effect_commands are authored.

### P0/P1/P2 validation and limitations
- Existing `tests/rework/combat/combat_foundation_smoke.gd` runs `runtime_cases.gd` via a preload, so A's existing Linux/Windows rework smoke hook includes the new live logic regressions **after** this stacked PR is incorporated into a branch with the CI workflow from main.
- `collision_grid.gd` broadphases actors in XZ with stable ID order and computes swept projectile/circle hits, avoiding high-speed tunneling on the plane. This is not mesh-vs-bullet or wall physics. `enemy_ai.gd` exposes chase/retreat/attack range selection; D still defines content/terrain.
- `Events.drain_batch(quota,handler)` defers overflow to later ticks without removing it, and `Events.drain(handler)` retains its unlimited semantics. Legitimate proc ancestry can have many stages, while exact repeated effect+actor+target cycles are rejected. Recycled projectile dictionaries are bounded purely for memory/performance. Frame/kill statistics are read-only counters; no VFX RNG enters damage.
- **[미확정]** When both on-hit and omni-vamp exist on one event, the default `vamp_overlap_policy=unresolved` emits `vamp_policy_pending` instead of applying an unapproved stack formula; A may explicitly set `sum`, `omni_only`, or `on_hit_only` as a temporary QA configuration. HP-only overkill and shields remain unresolved. 3D terrain collision, enemy pathfinding around walls, skill categories/animation, plugin catalogue numeric data, projectile VFX, reward/XP/save receipts, HUD, actual Windows input/perf QA require A/C/D/E follow-on work.

### Example programmatic smoke calls
```gdscript
var sim = preload("res://scripts/rework/combat/combat_runtime.gd").new(42, {"encounter_id":"cpu-01"})
sim.register_actor("vaccine", "player", Vector3.ZERO, {}, {"player_controlled":true})
sim.register_actor("virus", "enemy", Vector3(5,0,0), {}, {})
var notices = sim.step_fixed({"vaccine": {"move": Vector2.RIGHT,
    "aim_world": Vector3(5,0,0), "fire": true}})
```

### P2 event coverage / performance details
- B now emits `basic_attack` on firing, `dash` on successful movement dash, `periodic` only for actors carrying plugins, `damage_taken` with post-hit `health_fraction`, and `kill` once on a lethal HP transition. Critical hits appear as `critical=true` on `hit` presentation events and processed damage records; C can filter by this field.
- C trigger naming covers `projectile_hit, damage_dealt, skill_damage, skill_cast, on_hit_proc_count, health_threshold, periodic, mobility_or_reveal, dash, dash_or_blink, kill, passive_and_damage_taken, on_hit_proc, on_hit_proc_or_kill, basic_attack`. `passive` uses numeric passive offsets. `on_acquire` awaits E's exactly-once claim to avoid duplicate acquisition rewards.
- Every on-hit proc has opt-in rights from C's `can_trigger_on_hit_effects`. Numeric `on_hit_damage_efficiency` of a plugin bullet modifies the *resulting downstream proc damage*, not the original bonus projectile damage. C must supply target radius/range and trigger threshold data explicitly; no invented defaults from Korean summaries.
- `Events.drain_batch` keeps a queue cursor to avoid quadratic `pop_front` overhead. Its per-tick quota only shifts processing to later frames and never discards overflow. Collision broadphase returns stable actor ID order, then sorts swept hit fractions, independent of rendering order.
- C action `damage` with `target_policy=nearest_other_enemy` may optionally supply `max_range` in world units. No map wall line-of-sight test is claimed: D/A needs a physics query before enabling area effects through closed terrain.
- Enemy steering is a simple chase/retreat range policy, not a navmesh solver. A/D provides collision/nav and enemy geometry for production. Continuous projectile math covers dynamic XZ actor circles but not 3D wall/mesh intersection.
