# C P4 combat integration contract

Parent: #52 · issue: #57 · stacked Draft PR #67.

The P4 world owns **logical XY 1280×720**, 60 simulated ticks per second. Renderer actors use `RuntimeConfig.logical_to_world(logical_xy)` or B's `LegacyViewport` mapper; never rely on Godot RigidBody collision for damage.

### B's existing P3 player

B exposes `combat_snapshot()`, `simulate_tick()`, `skill_requested` and `change_hp()`. A's eventual world coordinator owns the order:

1. P3 player simulates its 60Hz movement, dash, pointer aim and invulnerability.
2. `PlayerBridge.read_player(combat, player_node)` copies logical position, aim, HP/MP and invulnerability. Set software statistics by `combat.set_player_snapshot({"stats": ...})` separately.
3. `CombatSim.step_fixed()` moves enemies, processes deterministic combat ticks and attacks.
4. `PlayerBridge.write_player(combat, player_node)` commits damage, heal, mana and 0.9s damage invulnerability back to the P3 instance. P3 does **not** lose dash invulnerability via a double countdown.
5. Connect `skill_requested` to `PlayerBridge.try_cast(combat, player_node, kind)`. Signal `player_hit(damage, infection)` is the **only** direct-hit infection source. Infection DoT does not recursively apply.

### Sector handoff

On entry: `campaign.enter_sector(id)`; `plan=campaign.start_wave()`; `combat.begin_wave(plan.wave_index, plan.spawns, plan.boss_kind)`. Each physics tick call `campaign.tick_infection(1.0/60.0)`, and on confirmed player hit use `campaign.apply_infection(infection)` exactly once. When `combat.wave_ready` fires, call `campaign.confirm_wave(combat.enemies.size(), combat.spawn_queue.size(), combat.boss_defeated)`, then handle optional software/reward UI. On retreat call `combat.abort_wave()` and `campaign.retreat()`.

### Validation limitations

- `godot/tests/combat_smoke.gd` verifies deterministic tick behavior, armor/skills/DoT and boss gating; not actual in-game FPS, GPU, mouse calibration or Python `random.Random` critical-hit trace.
- Python random choice parity for **sector wave planning** is implemented in P5 `python_mt.gd`, not the enemy combat RNG. Exact shooter-cooldown and critical-hit sequence remain QA parity work.
- Independent combat lab `res://scenes/combat/combat_lab.tscn` permits hands-on primitive-3D combat without replacing shared main or B input. It is a QA harness, not the final game.
