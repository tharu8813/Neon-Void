# C P5 4-sector implementation & integration contract

Related: #58, #57, #52. Draft PR #71 stacks onto P4 #67, which stacks onto P1 #65. Do not merge while P0/P1/P2/P3/P4 prerequisites are unapproved.

## Smoke and visual lab

- CI (Linux + Windows): `godot --headless --path godot --editor --import --quit` followed by `godot --headless --path godot --script res://tests/sector_smoke.gd`.
- Golden: each of 20 `CPU/MEMORY/STORAGE/NETWORK` 1–5 wave type sequences is validated against the frozen Python planner for seed 42. Example seed 0 boss-support sequence is also covered. This checks schedule selection but not all combat RNG or human gameplay equivalence.
- Local manual QA: open `res://scenes/sectors/sector_lab.tscn` in Godot 4.7.2 and Run Current Scene. This is an **independent placeholder 3D QA harness**, not A's final hub. WASD/arrows move, F near a gate enters a sector or advances to the next wave, R retreats from in-progress/clean rooms, Q/E casts P4 skills, and F near the hub purification station applies one of three charges. The four gates are at frozen world logical coordinates, converted to XZ at 0.01 world units/pixel.
- The standalone scene intentionally uses a simplified test-only walking loop, not B's P3 dash/obstacle collisions, and the UI is an in-lab diagnostics overlay. A owns the shared main scene, real gate/terminal art and full gameplay integration.

## Required world integration (A)

1. Keep P3's 60Hz logical movement/aim authoritative, with B's PlayerBridge input contract in `godot/docs/C_COMBAT_INTEGRATION.md`.
2. On physical gate use `campaign.enter_sector(id)` (supports case-insensitive IDs). If clean, show CLEAN status and do **not** start a sixth wave.
3. Otherwise `plan=campaign.start_wave()`, then `combat.begin_wave(plan.wave_index, plan.spawns, plan.boss_kind)`. All `spawns` entries are dictionaries `{"at": float seconds, "kind": string}`.
4. On each unpaused 60Hz tick: combat player snapshot, `combat.step_fixed()`, `campaign.tick_infection(1.0/60.0)`. Connect P4 `player_hit(damage, infection)` to `campaign.apply_infection(infection)` once per confirmed hit.
5. On `combat.wave_ready(boss_defeated)` callback, **also** verify zero live enemies AND zero pending spawns through `campaign.confirm_wave(combat.enemies.size(), combat.spawn_queue.size(), boss_defeated)`. A confirmed wave is now counted and emits a stable receipt `run_id:sector_id:wave_index`, but the campaign does not grant XP, software or disk receipts.
6. Pause the combat while selecting wave rewards; after the next-wave action, call `campaign.start_wave()` again. On a physical exit gate, call `combat.abort_wave()` + `campaign.retreat()` so a partial active wave is discarded but completed waves, player HP/MP and infection remain.
7. In OS hub a purification station consumes at most **three charges per deployment** as defined by the original game world; each charge purifies 32 and repairs integrity by 9. A must store charge state in its world/checkpoint owner, not in P5 campaign. When integrity reaches zero, trigger the original emergency extraction without installing permanent software.
8. OS CLEAN is an event, **not automatic extraction**. A/P8 own the in-world STATUS terminal, XP transaction/replay protection, software selection and durable migration. No files named `save.json`, `settings.json`, `profile.json`, `antivirus_db.json`, or `os_checkpoint.json` are touched by this module.

## Remaining parity gates

The headless lab proves script/scene import, deterministic content, progression and integrity arithmetic. It does **not** prove physical Windows graphics/DPI, mouse mapping to pixels across resolutions, identical combat damage/crit trace (Godot combat RNG differs from Python), spatial collision feel, full P6 software/UI, P8 save migration, or a 30–60 minute complete manual playthrough. These are blockers before final P4/P5 acceptance in #52.
