# Hub and motherboard contract, D-owned follow-up

Second isolated feature (#98), based on current main and intentionally separate from PR #101. A reviews/merges.

## Flow
- `res://scenes/rework/world/defender_hub.tscn` is a physical 26×18 XZ room with accessible vaccine, plugin database, skill loadout, dispatch terminals and floor/wall collider geometry. The scene never touches permanent data. `hub_world.gd.request_interaction(player_world)` finds the nearest terminal within its radius and emits `terminal_requested({schema:1,type:"hub_terminal_requested",terminal_id,action})`. A must connect actual player input and E interfaces.
- `res://scenes/rework/world/motherboard.tscn` is a 28×19 XZ staging room with fixed accessible gates for CPU, GPU, RAM, SSD, HDD and three intro-enemy spawn markers. A supplies `setup_run(run_id, mission_snapshot)` from the seeded planner in PR #101. B spawns and validates weak intro enemies, then calls `confirm_intro_enemy_defeated(id, receipt_id)` once per real kill.
- `motherboard_layout.gd` holds independently testable gate lock-state and emits `board_event(Dictionary)`. Only all three intro kills unlock gates. `request_gate_near_player(player_world)` denies remote gate access and emits `hardware_gate_requested` after unlock, including `run_id,region_id,region_plan`. A loads a hardware scene, B handles combat, D extraction state sends clearance facts to E. `return_to_board(region_id)` resets the gate transition lock without granting cleanse/rewards.
- Current fixed gate positions, room dimensions, intro enemy count and visuals are **[draft v0.1]**. The final GDD rules (five regions, free order after intro) are preserved. The preview does not include a player or combat AI and is **not yet end-to-end playable**.

## Safety/validation
- No `project.godot`, existing scenes, A/B/C/E paths, Python files or save data changed.
- Manual new test: `godot --headless --path . --script res://tests/rework/world/hub_motherboard_smoke.gd`
- Linux/Windows CI cannot be assumed to execute this new smoke until A updates its workflows.
- Windows QA: player movement and terminal approach, collision, intro encounter completion (B), gate hints/lock state (A), free order, exit/re-enter policy (unconfirmed), performance.
