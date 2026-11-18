# S01 · C plugin contract and acceptance handoff

Owner: C / #149. Base: `integration/rework-s01-playable-core-20261118`. Review/merge authority: A. Status: **partial code fix + added regression coverage, integration acceptance pending**.

## Actual execution routes (do not conflate)

1. Normal REWORK scene: `rework_pilot.gd` obtains `Gateway.snapshot` and calls `production_b_world_facade.configure_authorized_build`, which eventually configures B `c_effect_adapter.gd` + C `plugin_effect_runtime.gd`. A calls `upgrade_plugin_after_ack` only after E source receipt confirmation. This is the **production path**.
2. `plugin_b_live_runtime.gd`: a separate C subclass of B's runtime. `plugin_b_live_cases.gd` covers 26 combat state effects at level 1; `nv_c_random_rare` is a pure E reward, not a combat attack. **Do not activate both adapters.**
3. `plugin_b_combat_sink.gd`: C's transactional B damage/status sink used by the plugin effect assertions. S01 removes its old standalone rare request forwarding and permits a valid zero-hit lightning chain against a solo target, consistent with the B production adapter's empty-chain behavior. This is not the entire production combat integration.

## S01 additions and exact reproducible checks

- `plugin_live_view.gd::describe(confirmed_E_levels, deployed_B_levels, runtime_state, tick)`: **read-only** 27-row DTO for A. It refuses unknown/over-cap plugin IDs and any B level exceeding E. It labels a pending post-ACK B sync separately from actually deployed levels. Optional `runtime_state` must come from C's runtime, not UI predictions. Missing data is `state_unknown`, never fabricated activation.
- `plugin_effect_assertions.gd`: verifies 27 catalog DTO rows, 27 E-before-B gates, tampering and missing authorization rejection. Exercises the C B damage/status sink on isolated-target chain lightning, rejected fake rare award, harmless read-only notice, and rollback. Existing exact E source+bonus transaction tests remain unchanged.
- `plugin_s01_production_matrix.gd`: 8 shield level cases (periodic 3, skill 3, dash 2) against actual B `combat_runtime.gd` + production `c_effect_adapter.gd`. Uses genuine B skill/dash/tick event generation, verifies C's per-level shield value and exactly 5 true-damage HP spill. Fixtures are synthetic **not evidence of per-card E ACK or real 3D rendering**.
- All tests are invoked by `tests/rework/plugins/plugin_reward_smoke.gd` (existing Windows/Linux plugin CI). Command from repo root: `godot --headless --path . --script res://tests/rework/plugins/plugin_reward_smoke.gd`.

## A / B / E interface requests

- **A:** on E-verified load, pass E plugin level map to B; after reward, update B only after the same E revision/receipt is durable. For UI, `describe(E_levels, B.effects.active_levels(), B.effects.c_engine.state_snapshot(), fixed_tick)` after validating both belong to the same run. Never derive E levels from a staged card or the C offer UI. A should show `waiting_for_B_sync` and handle an ACK timeout as an unresolved transaction, never a fresh card.
- **B:** provide production event/activation telemetry if A wants to display *actual triggering* vs simply *armed*. C DTO can show armed/passive/known self-state, but **does not invent historical procs, shield layers or projectile activity**. Boss control resist, laser+boomerang precedence and damage/vamp ordering require B/A approval. Production `nv_r_low_hp_knockback` is a request notice; actual world impulse needs A/B verification.
- **E:** `nv_c_random_rare` is only E's two-receipt atomic source transaction; C must neither roll a separate rare nor grant a second copy on notice. Store C's offer packet (seed, RNG cursor, cards, rerolls, five-screen pity, pending receipt) with an atomic E checkpoint and reconcile on resume. Level-2 additional rare count and exhausted pool fallback remain unapproved. A/E need actual crash/ACK-loss scene replay proof.
- **A Windows QA:** one new run; 27 IDs at each level through **E ACK → B attach → live hit/trigger → observable state**; all five zones; event recursion, saved rerolls, one-target boss, 3 shields, rare bonus; manual game footage/console evidence. The C-only suite cannot claim this checkpoint complete.

## Known non-acceptance items

The existing 27-catalogue definitions and level effect arrays are present but **full 27-by-every-level production gameplay** and **actual Windows E→B→3D** evidence are outstanding. Do not mark #149 fully done solely on a green headless job. No mythic entries or approved mythic odds have been added. Existing IDs, E persistence schema, Python game and other teams' source files are unchanged.

## Environmental damage → C trigger / shield contract (S01 follow-up)

**Verified fixture route (not production GPU coverage):** `tests/rework/plugins/plugin_environment_damage_cases.gd` registers a *synthetic* non-vaccine `ENV_CONTACT` B actor, submits a B-authoritative damage event with `source_tags=["environment_damage","hazard_contact"]` and a stable collision marker, calls real B `step_fixed`, then runs the **production** `c_effect_adapter.after_fixed_step(b.last_processed_events)`. No C-only fake HP math. Do not register this test dummy actor in actual missions.

- All periodic/skill/dash shield levels should absorb B damage before HP; full absorption should NOT emit `damage_taken` (B exits on zero actual damage), while partial absorption should emit exactly one `damage_taken` with the **actual HP spill**. Depleted shield cannot be reused.
- `nv_c_breakable_sprint` breaks only on actual HP loss. `nv_c_low_hp_emergency` reacts to a genuine low-HP `damage_taken` and can grant its proposed barrier. `nv_r_low_hp_knockback` requests a knockback notice only: real physical impulse is **NOT** claimed. No environment actor attack may become a vaccine `on_hit`/chain-lightning proc.
- Shields in these cases use the provisional `post_resistance` additive policy. Interaction with invulnerability/crit/DoT/vamp, status mitigation and fully blocked hit feedback still need A/B design approval; tests do not settle balance.
- This contract is safe against repeat C dispatch only to the extent the B event router is authoritative. Collision contact de-duplication / continuous hazard tick policy is **D/B-owned**, not faked with C `_seen`.

**Blocking real GPU path (D/B/A):** `scripts/rework/world/gpu_objective_session.gd::tick_player` currently has beam timing and dodge acknowledgement but explicitly leaves a beam HIT without authoritative HP damage. To complete the gameplay chain:

1. **D** should emit one run-scoped, region- and cycle-scoped hazard contact fact for `in_arena && in_beam`, with authoritative beat/tick, collision key, hazard ID, target ID, and damage configuration approved by B/A. D must not directly mutate `VACCINE.stats.health`, append C effect notices, or create E receipts.
2. **B** should validate active encounter/run + actor/target + hazard profile, deduplicate each impact at the intended frequency and expose an environment-damage submission API that passes through **B `Damage.calculate` → `damage_absorber` → `apply_damage` → `damage_taken`**, preserving environment provenance in source tags/presentation. Existing B `damage_taken` events currently replace source tags with `["damage_taken"]`, so A cannot identify environment provenance from them; B must explicitly define how it carries metadata.
3. **A** should connect D's signal to B only after checking current encounter identity, display authoritative B HP/shield notices, and pass B `last_processed_events` once per 60Hz tick to C production adapter. Do not attach C's experimental subclass and production adapter to the same actor.
4. **E** should receive only existing valid progression consequences of actual death/objective completion. Contact should not create or reroll plugin reward receipts.
5. **Acceptance:** run an actual GPU beam hit and evade in normal 3D REWORK, with E-confirmed shields, observed B HP and barrier loss, one damage_taken on spill, no on_hit, and correct B/A scene notices, including repeated contact at the approved interval. The headless C test alone cannot satisfy this requirement.

