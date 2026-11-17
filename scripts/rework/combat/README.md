# B / Combat v1.0 provisional integration contract

Source: GDD v1.0 §§6–8,11 and [rework issue #96](https://github.com/tharu8813/Neon-Void/issues/96).
**This is a proposed API for A/C/D/E review, not an approved cross-team contract.**
All modules extend `RefCounted`, are detached from legacy scenes and run at a caller-controlled fixed 60 Hz. No autoloads, rendering calls, saves, GPU RNG, input bindings or gameplay scene mutations.

## Files / integration calls

- `stats.gd`: `Stats.make(overrides)`, one shared player/enemy dictionary shape. Rate fields (`crit_chance`, `tenacity`, `physical_pen_percent`, `omni_vamp`) are fractions, not 0–100 percentages.
- `damage.gd`: `Damage.calculate(attacker_stats, defender_stats, attack, critical=false)` returns raw/mitigated figures; `Damage.apply_damage(defender, result, invulnerable)` updates HP only; `Damage.vamp_components(attacker, attack, actual_damage)` returns **separate**, not automatically stacked healing amounts.
- `statuses.gd`: `Statuses.apply(state, definition)`, `active_flags(state)`, `stat_offsets(state)`, `tick(state)`. Statuses are authored as data with `stack_mode` one of refresh/replace/stack/independent. `tick` produces DoT damage requests and HP regeneration requests; apply through damage system, never directly through VFX.
- `events.gd`: `Events.new()`, `set_tick(tick)`, `submit_root(payload)`, `submit_proc(parent_event, effect_id, payload, icd_ticks)`, `drain(handler)`. `handler(event) -> Array[{effect_id, event, icd_ticks}]`. Source root must carry stable `hit_key` (projectile/attack collision identifier) and `target_id` to prevent duplicate hits. Root proc path is empty; children extend signature `effect_id|target_id`. Same effect on **different targets** is allowed; repeating the same pair inside the same ancestry is rejected; independent attack roots have fresh ancestries. Per-effect/target ICD is caller-supplied in ticks. `allowed_proc_triggers` on parent permits only listed child `trigger_kind` values.
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
