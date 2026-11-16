# B Player/Input P3 integration contract (#56)

Scope: scripts/player, scripts/input, scenes/player and tests/p3_player_parity.gd.
Do not modify A-owned scripts/main.gd, scenes/main.tscn, project.godot, Python or any save files.

## Standalone integration
- Run res://scenes/player/parity_playground.tscn for an isolated 3D player + input + custom cursor preview. This is NOT the main game loop or a gameplay completion claim.
- Instantiate res://scenes/player/legacy_player.tscn with the LegacyCamera and LegacyInput nodes in the owning scene. Wire player.set_input_source(input_node), player.camera_source=camera, and camera.follow_target=player.
- player.simulate_tick(dt, move_direction, dash_key_edge, scaled_dt=-1) is deterministic, matching the frozen Python 60Hz speed, interpolation, hit radius, 940px/s dash, 0.145s dash state and 0.32s invulnerability. In hub mode use player.hub_mode=true / camera.hub_mode=true (world 2400x1560, four pillar rectangles). Battle arena clips within 1280x720. Godot move_and_slide intentionally NOT used.
- Input default controls.py nine unique rebindable actions + permanent arrows and Shift. M/R/O are reserved. Escape pause, Tab inventory, F11 fullscreen and Alt+Enter borderless are surfaced via signals for A's UI dispatcher, NOT directly applied in this module. On window focus lost, keys and edge requests clear. Disable input while modal/pause through set_enabled(false) and simulation_enabled=false.
- Mouse: legacy_input.set_pointer_logical(Vector2) accepts already-scaled Godot viewport pixels; set_pointer_native(raw_client_point,client_size) is only for raw client-area OS pixels, with black bars rejected. A must ensure fixed 1280x720 camera viewport. The custom cursor is an optional CanvasLayer overlay; UI/menu ownership remains with A.

## For C combat integration
- player.combat_snapshot() exposes logical_position, world_position, velocity_px, aim_radians/aim_direction, hp/max_hp, mana/max_mana, radius_px, dash_t/dash_dir/dash_charges, invulnerability_t.
- Signals: position_changed(Vector2), aim_changed(angle,Vector2), dash_started(Vector2), health_changed(hp,max_hp), skill_requested("pulse"|"guard"), interact_requested(). C owns mana costs, real skill cooldowns, combat damage, stun/hitstop and other gameplay state.
- Stats may be set by combat/software upgrades: speed_px, dash_max (cap charges separately), dash_cd, hp/max_hp, mana/max_mana. No legacy user settings read/write.
- World obstacles use Collision.move_hub mathematical axis-by-axis circle/rect equivalence; Area3D child provides a hit detection volume, not an engine physics movement substitute.
- CI exercises input edges, movement, dash, bounds, pillars and prefab parse on Linux/Windows. Rendering, UI routing, DPI, 30-minute play and feel still require actual Windows manual QA. #56 is not complete until the test artifacts and visual/reference parity are reviewed.
