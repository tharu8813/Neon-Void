# D CPU world: formal B/C combat handoff (v1.0)

**Scope:** D owns this integration layer. A owns `scenes/rework/integration/rework_pilot.tscn`, `rework_pilot.gd`, root scene and CI workflow. B owns `scripts/rework/combat/`. C owns `scripts/rework/plugins/`. E owns persistence. Never merge this cross-team stack without A's review.

## Entry paths and source of truth

| Route | Selected combat implementation | Reward persistence |
| --- | --- | --- |
| `scenes/rework/world/cpu_vertical_slice.tscn` F6 | `demo_mode=true`: D-only `demo_combat_bridge.gd` | No, explicitly fake preview ACK |
| `scenes/rework/integration/rework_pilot.tscn` F6 | `demo_mode=false`: **B** `combat_runtime.gd` through `combat_node_bridge.gd` and D `live_b_combat_world.gd` | **E** atomic `start_mission`, `mission_xp`, objectives, clean, `extract` / `die` |

`world_combat_router.gd` is a stable Node3D facade. It always exposes the same `enemy_defeated(actor_id, receipt_id)`, `player_died()`, `health_updated(hp,max_hp)` signals previously consumed by A. The pilot connects in `_ready` **before** the mission begins, so the same router instance must select its internal B mode at `begin_encounter(..., {formal:true})`. Keep A's signal wire and E ACK ordering unchanged.

This change intentionally does **not** create a second damage formula or another HP ledger. The old `_apply_hit` test fixture is only forwarded while the facade is in preview mode. Calling it in live mode is inert.

## B actor/control handoff

- B `combat_node_bridge.setup(encounter_id,seed)`, `register_node`, `set_input`, `advance_one_tick` handles the fixed 60Hz simulation; D registers physical `CharacterBody3D` bodies. `drive_actor_nodes=true` uses B movement decisions while Godot `move_and_slide` enforces D wall collision. The player motor owns normal WASD movement, so its B command contains `move=Vector2.ZERO` to avoid double movement. Root/stopped status flags are derived from B's `statuses.gd` and disable the D motor.
- D projects the pointer from the **rendered camera** to XZ, then sends `aim_world` to B. B creates/steps swept projectiles, calculates physical/magic/true HP damage and crit, consumes mana/cooldowns, dispatches C-type numeric plugin commands and declares deaths.
- For CPU mobs D supplies stable `CPU-VIRUS-00`…`09`, `CPU-BOSS` and real 3D spawn positions. B `enemy_ai.gd.decide` supplies approach/range intentions; when navigating the CPU maze, D chooses a reachable BFS waypoint, while B still applies movement speed/CC and projectile damage. Enemy steering uses player-controlled *input mode* in B solely to permit terrain-aware external AI intents, **not** as a faction change; each enemy remains on B team `enemy`.
- B `combat_actor_died(id,killer)` generates only one D receipt `world:<run_id>:<actor_id>:death` per actor. B's volatile event IDs are not reward receipt IDs. D's existing CPU objective gate uses these receipts to count real kills and unlock the boss; boss death emits the existing `boss_defeated` and `hardware_cleansed` world facts to A/E, which must ACK durable transactions before extraction.
- A/E must still enforce that mission start was durably stored **before** invoking `world.confirm_mission_started(run_id,seed)`. Failed/unknown receipts freeze or require reconciliation. This D PR changes neither A pilot persistence nor E rules.

## C effects/skills: available API and limitations

`world.combat.configure_authorized_build(levels, instances, skills)` takes:
- `levels`: C plugin ID → integer level from the **E-acknowledged** carried/obtained record.
- `instances`: actual B runtime plugin metadata/numeric `effect_commands` using C's approved definition, not a translated localized text description.
- `skills`: authorized B skill definitions, at most three equipped. Q/E/R request slots 0/1/2; Space issues movement-direction dash only with an **A-authorized nonempty** dash definition supplied to B.

D constructs a fresh C `plugin_effect_runtime.gd` from the levels and uses `derived_stats` to initialize B actor's passives. B itself executes `set_actor_plugins`, `learn_skill`, status math and mana. Tests verify C passive and one numeric status-command fixture reaches B via actual hits; **this is not proof that all 27 C effects execute in the live scene**.

To fully activate the C catalogue in gameplay, **A/C must supply actual E-acknowledged numeric plugin instances** and call `configure_authorized_build` after `prepare_card → E commit → C confirm_card` (or mission load). C's own 27-effect command sink writes to a separate C world dictionary; feeding it alongside B's live HP ledger without an atomic bridge would double-apply or desync damage. This D PR deliberately does **not** do that.

When a new plugin is approved mid-encounter, B's `set_actor_plugins` may update numeric procs. B currently lacks an approved atomic API to refresh base stats while preserving HP/cooldowns, so newly acquired C passive stats are applied on the next encounter. A/B should agree a safe live-stat refresh method rather than mutating B internals.

## Known B integration blockers for A/B before release

1. **Terrain projectile collision:** B's swept XZ projectile grid checks actor circles, not D wall tiles. D currently blocks enemy firing through walls and uses physical bodies for actors, but bullets already in flight may still pass through maze walls. B needs a **world collision query/hit-callback before resolving a projectile's actor hits**. Do not claim wall-blocked bullets until that hook is implemented.
2. **Skill/catalog authorization:** A pilot has not yet wired its E-committed new plugin choices into `configure_authorized_build`. No fabricated plugin grant, numeric effect value or default learned skill.
3. **Actor steering:** BFS target navigation is D-provided; run a Windows 20-minute maze/corner/stuck-actor QA, boss movement/collision and camera follow at 100–200% DPI.
4. **B movement dash:** D forwards Space requests, but A/B must configure approved `dash_definition` with distance/cooldown; no invented balance defaults.

## Cross-platform regression & real QA

The existing A workflow on branch #115 includes `tests/rework/world/cpu_vertical_slice_smoke.gd`. D extends it with:
- preview D CPU regression (old demo uses _apply_hit fixtures only);
- formal B actual swept projectile→damage→C-shaped status→skill/mana→real death, rooted action gate and stable one-shot enemy receipt;
- A **real** synthetic persisted pilot: E slot created with a unique path, mission durably started, B projectile kills an intro enemy, E records exactly 8 mission XP with an increased revision, then full CPU B kills/defense/boss/world clear/extract. E approves every objective and terminal receipt. Synthetic slot cleanup is restricted to the exact newly generated path.
- no writes to legacy user files, no modification to A `rework_pilot.gd`.

Manual Windows QA (all unverified by headless CI):
- Godot F6 launch of `rework_pilot.tscn`, no change to legacy `main.tscn`.
- WASD movement, root/stun, cursor tracking, enemy maze pathfinding, hitbox and projectile impact vs walls (currently known blocker).
- Q/E/R with E-authorized skills and mana/cooldown, Space dash after B/A config, B boss attacks and real death → E clone/4th-life fence.
- C plugin effects after E card ACK, online C/B sync without duplicate HP/proc, seed determinism, performance at high projectile loads.
- E receipts, disk full/permission failure/forced termination/restart without fake success or duplicate XP/learning.
- 1280×720, 4:3 / 16:9 / 21:9, DPI 100–200%, resize, Alt+Tab, display/GPU/audio, p50/p95/p99 frame-time.
- 8–12 minute CPU clear time and 40–60 minute five-hardware target are **unverified balancing goals**.

Hardware content finished in this D PR: **CPU only**. GPU/RAM/SSD/HDD remain modeled but their playable regions and unique bosses/hazards are not implemented here.
