# B: E-authorized permanent combat progression and skill contract

**Owner:** B; based on A #137 head `df78ac7746d47c102ab65ed66e476456976514f8`. This branch is stacked over A #137, which already includes #136 and B #126. A alone decides cherry-pick/integration/merge; **do not merge directly to main**.

## E → A → B, trusted source boundary

1. A calls `Gateway.snapshot(slot_path)` **after** a successful E save receipt/readback, not a UI allocation draft or pending card.
2. For each hardware encounter (CPU/GPU/RAM/SSD/HDD or motherboard), A must pass that complete Gateway response directly to `bridge.configure_authorized_progression(loaded)`. B does not read or write E storage. A's existing `production_b_world_facade.gd` should forward the call to its `bridge` object, and report a rejected reason by `integration_failed` before allowing the first physics tick.
3. Invoke after `begin_encounter` and **before** first simulation tick, or stage it on a B bridge before `begin_encounter`. For example:

```gdscript
var loaded: Dictionary = Gateway.snapshot(slot_path)
if not loaded.get("ok", false):
    _halt("E_progression_snapshot_missing")
    return
var combat = world.get("combat")
var ack: Dictionary = combat.bridge.configure_authorized_progression(loaded)
if not ack.get("ok", false):
    _halt("B_progression_rejected:" + str(ack.get("reason", "")))
    return
# Existing E-authorized plugins still use configure_authorized_build(...)
```

4. B applies `invested_stats`, `known_skills`, and `equipped_skills` only from the E response. No ad hoc stat dict API or skill-definition injection is used. B rejects bad IDs, negative/noninteger/nonfinite investment counts, undefined equipped skills, duplicates or >3 slots, absent snapshot, destroyed vaccine and mid-encounter loadout changes. A must still verify `vaccine_id`, `generation`, `revision`, `mission.run_id` against its active E session, since B is not a cryptographic E authenticator.
5. After a zone switch B `reset()` erases temporary status, HP, mana, cooldowns and the previous authorization. A **must** read/check the latest E snapshot again and reapply before combat starts. This prevents accumulation across hardware regions. Mid-mission newly learned skill changes require a separate A/E ACK-reviewed transaction path; the B endpoint deliberately rejects arbitrary direct `learn_active_skill` injection once a formal E profile has been configured.

## Active skill registry

B registry `scripts/rework/skills/skill_registry.gd` defines 9 explicitly named **BALANCE_DRAFT / provisional** skills, not an approved full GDD pool. Only the skill ID is persisted by E; shape/heal/blink data below is B-owned runtime data and never a save migration.

| E skill ID | Proposed effect | Cost / cooldown |
| --- | --- | --- |
| `nv_skill_arc_pulse` | Radial magic burst, 4 projectiles, base 8 + 0.75 AP, not crit | 18 mana / 90 ticks |
| `nv_skill_penetrator` | Physical rail, pierces up to 4, base 5 + 1.2 AD, crit allowed | 22 mana / 120 ticks |
| `nv_skill_root_packet` | Magic projectile, base 4 + 0.5 AP, applies 90-tick root before CC resistance | 30 mana / 180 ticks |
| `nv_skill_overclock` | Self buff: +2.0 attack speed, +1.5 move speed for 300 ticks | 24 mana / 300 ticks |
| `nv_skill_patch_kit` | Self heal 22% max HP + 25 mana | 20 mana / 420 ticks |
| `nv_skill_blink_step` | 6.5m aim-direction blink clipped by D floor tiles, +1.5 move speed for 120 ticks | 26 mana / 300 ticks |
| `nv_skill_scatter` | 3-projectile physical fan, base 4 + 0.8 AD, crit allowed | 20 mana / 150 ticks |
| `nv_skill_emp_ring` | Radial magic burst of 8, base 6 + 0.6 AP, applies 90-tick slow | 34 mana / 240 ticks |
| `nv_skill_seeker_swarm` | 2 homing magic projectiles, base 6 + 0.7 AP, crit allowed | 28 mana / 210 ticks |

These proposals must be reviewed by A. E may store unknown skill IDs without B failing to load a **different** equipped skill; equipping an ID absent from the B registry explicitly fails. No invented fallback attack.

Mid-mission discovery uses `apply_authorized_skill_loadout(fresh_E_snapshot)`. B rejects it unless the snapshot revision is newer than the installed one, `vaccine_id`/`generation` match, `invested_stats` are byte-identical, and the new loadout validates. It replaces only the three active slots; it never changes HP, stats, mana pool or rewards. A must pass a snapshot read back **after** the E `learn_skill` ACK.

The existing skill pipeline retains haste-adjusted nonzero cooldown, 60Hz regen, 3 slots, silence/stopped/root controls, critical flags, C on-skill triggers, B hit damage/status and deterministic impact. New observable notices: `skill_cast` (slot, skill ID, mana_spent, ready_at_tick), `skill_rejected` (slot, reason), and `skill_impact` (skill ID, actual_damage, target_id, event_id). A can query `bridge.skill_snapshot()`, `bridge.actor_snapshot()`, `bridge.authorized_growth_snapshot()`; all return copies. Existing `simulation.snapshot` keeps `stats`, `slots`, `mana` and adds `effective_stats` and `skill_state`.

## All 22 permanent E stat IDs

B `permanent_growth.gd::INCREMENTS` maps E `STAT_COSTS` to B keys and GDD section 13.1 increments. Units: HP +5, AD +0.25, AP +1, armor/MR +2, move +0.05 world units/sec, attack speed +0.05 shots/sec, projectile-speed multiplier +0.02, crit chance +0.005 fraction, crit damage multiplier +0.01, haste +2 rating, luck +0.005 fraction, mana +5, regen +0.25 mana/sec, projectile size +1, flat penetration +0.5, percent penetration +0.0025 fraction, hit-vamp +0.0025, omni-vamp +0.0025, tenacity +0.01 fraction. Exact point costs remain E-owned, not recalculated in B.

### Calculation order

- Encounter baseline from B `balance_draft.player_stats` / `Stats.make` (GDD + assigned actor base).
- Apply **permanent investment increments** additively once, from an immutable baseline, to authoritative B actor `stats`.
- At runtime `combat_runtime._effective_stats`: apply temporary **status modifiers** to B base stats, then legacy B passive offsets if configured, then **C plugin derived_stats** `stat_provider`. This preserves C as the sole production plugin recipe source. Crit, penetration, physical/magic/true and vamp use that resulting effective stat.
- **HP and mana on encounter start:** full after investment (e.g. +10 max HP => +10 current spawn HP); never refill an active actor mid-encounter with profile changes. Runtime damage/status effects retain current actual HP and skill mana.
- No arbitrary caps on C/plugin effects. E level counts are validated; effective chance and vamp/tenacity fractions must remain in [0,1]. If A wants different overflow/rate policy, update B balance proposal, not E storage.
- Crit damage is 1.20 + fractional 0.01 per point, not 20% +1% raw. Fire rate is attacks/sec, not a multiplier.

## A/E open integration tasks

- **A**: forward formal API on `production_b_world_facade`; in `rework_pilot.gd::_sync_authorized_build_from_E()`, pass loaded E snapshot and block the first tick until both B growth and C plugin acknowledgements succeeded; change read-only actor proxy to `bridge.actor_snapshot` so UI sees shield and effective stats. Do not edit E UI drafts or other team files in this B PR.
- **A/E**: approve the remaining discovery UX/rules: draft reroll/skip policy, duplicate and full-pool behavior, and whether a mission-end draft is offered. The ACK bridge itself now exists as `apply_authorized_skill_loadout`; the live pilot offers only after `hardware_cleansed` is E-acknowledged.
- **A**: approve or replace the 9 BALANCE_DRAFT skill definitions and their numbers. Unknown E IDs cannot be silently treated as generic bullets.
- **A**: decide HP/mana refill policy for later real-time allocation changes (not permitted mid-encounter yet), stat soft/hard caps, and whether to surface effective vs raw stats in hub UI.
- **A/D**: verify actual five-zone entry, physical mouse aim/skill bindings and Windows gameplay FPS; CI headless smoke is not physical QA.

## Regression

`tests/rework/combat/permanent_growth_cases.gd` exercises real E Store + Gateway commands in a disposable synthetic user:// slot, then B 60Hz projectiles vs enemy HP, E permanent stats and max resources, skill projectiles/cooldowns/mana/root, rejected mutations, and zone reset/reapplication. Integrated into B's existing Linux/Windows Godot headless workflow. No persistent production or legacy saves are touched.
