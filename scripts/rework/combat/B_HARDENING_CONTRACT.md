# B combat hardening contract (Draft, 2026-11-18)

Owner: **B**. Stacked base: A recovery/pilot PR #122; changes here are only `scripts/rework/combat/**` and `tests/rework/combat/**`.

## Verified implementation intent

- Reuses the B 60 Hz `combat_runtime.gd`, `events.gd`, `production_cpu_adapter.gd` and `c_effect_adapter.gd`; does **not** introduce a second combat engine.
- B event admission defaults to 8,192 pending commands (configurable `max_pending_events`); separate work scheduling remains `event_work_per_tick=512`. Saturation rejects new commands and reports `bus.metrics.overflow_rejected` rather than consuming an unbounded amount of memory. **A must treat a nonzero overflow counter as a failed run, not a successful combat event.** The 2,048-event FIFO canary stays within the cap.
- B caps simultaneously active projectiles (default 4,096, configurable) and applies basic-fire backpressure. Derived projectile command batches must also be preflighted by the C/B adapter.
- The B production bridge clips swept projectiles against D's `Maps.is_floor(plan, cell)` contract by tile boundary traversal, including diagonal wall corners. D still owns the generated tile map, collision shapes and actual playable hardware scene.
- Tick profiling is observational only. `simulation.profiling_snapshot()` reports rolling mean/p95/p99 microseconds, sample count and queue saturation. This is **simulation time**, not GPU-presented FPS or measured frame delivery.

## Shield policy API (provisional, **not approved GDD balance**)

`production_cpu_adapter.configure_shield_policy(policy: Dictionary)` delegates to the C/B adapter. Call **after** `begin_encounter` and **before** any shield grant. Changing policy with active shield layers fails closed.

| Setting | Accepted | Current compatibility default |
| --- | --- | --- |
| `absorption` | `post_resistance`, `true_damage_bypasses`, `none` | `post_resistance` |
| `stacking` | `add`, `replace`, `strongest` | `add` |
| `duration_ticks` | integer >= 0, `0` means no expiry | `0` |
| `maximum_total` | float >= 0, `0` means no cap | `0` |

The default preserves the previous A shield-first canary but is not balance approval. C owns all amount/trigger recipes (`nv_c_periodic_shield`, `nv_c_skill_shield`, `nv_r_dash_shield`). On a successful C sink commit, B records an independently expiring layer with source ID, consumes older layers first and updates the world-facing `actor_shield` scalar once. Damage is calculated through B physical/magic/true resistance **before** shield absorption and B HP reduction; no heal-on-shield hacks. Invulnerability skips shield consumption. B DoT enters the same damage callback. Lifesteal applies to actual HP damage, not absorbed damage, under the existing unresolved `vamp_overlap_policy`.

Diagnostic API: `effects.shield_diagnostics()` includes policy, layers, pending C events and overflow metrics; `production_cpu_adapter.diagnostics()` exposes it and simulation profile alongside B/C counters.

### Decisions requested from A before release

1. Should true damage bypass barriers? Should shields block `on_hit`/damage-taken triggers when only the barrier takes damage?
2. Confirm default shield lifetime, stacking/refresh, maximum total and whether periodic refreshes existing layers or create independent stacks.
3. Resolve omni-vamp + on-hit vamp overlap, shielded DoT and posthumous leech ordering. Current HP-only healing path is intentionally not final.
4. Confirm projectile/event queue capacity and policy on overload (fail mission, pause and report, or prioritize) for device classes beyond CI.
5. Confirm which C engine is the production path: A's CPU pilot uses B `c_effect_adapter.gd` only; C `plugin_b_live_runtime.gd` is a separate experimental path and **must not** be instantiated simultaneously. D/A must keep numeric legacy instances absent.
6. C should own derived-proc projectile demand validation. E should not grant reward/kill receipts when combat has entered overflow fault state.

### Cross-owner dependencies

- **D:** `procedural_map.gd` `TILE_SIZE` and `Maps.is_floor` must correspond to spawned walls. New GPU/RAM/SSD/HDD boss specs require D enemy IDs, world objectives and attack telegraph visual contracts. B does not modify D scenes.
- **C:** `plugin_effect_runtime.gd` and `plugin_b_combat_sink.gd` remain the sole skill/dash/periodic shield recipe source. Changes to C command schema require a compatibility review.
- **E:** stored plugin levels, learned skills and awards arrive only after durable receipt ACK; B never writes mission saves.
- **A:** parent stacked branch is validation only, no direct `main` merge. A integrates selected B fixes after owner review and checks real input/HUD/FX/long-run Windows gameplay.

## Tests and remaining work

New `tests/rework/combat/queue_shield_cases.gd` runs under existing B headless CI entrypoint on Windows and Linux: bounded FIFO, replay after over-cap rejection, real projectile allocations, wall traversal, real HP/shield depletion/expiry, attack-speed pressure, and profiling metadata.

**Remaining P2/P3**: D's GPU/RAM/SSD/HDD content and full boss phases, live telegraph and animation hooks, 8–12 minute profiling across realistic hardware, high-fan-out effect stress/cycle profiling, physical Windows input and rendered performance checks. These are NOT implied by passing the unit suite.
